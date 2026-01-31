using InventoryBot.Application.Interfaces;
using InventoryBot.Domain.Entities;
using InventoryBot.Domain.Enums;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace InventoryBot.Application.Services;

public class UpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserRepository _userRepository;
    private readonly IAppConfigRepository _configRepository;
    private readonly LocalizationService _loc;
    private readonly ILogger<UpdateHandler> _logger;

    private static readonly Dictionary<long, string> _userStates = new(); 

    public UpdateHandler(
        ITelegramBotClient botClient,
        IUserRepository userRepository,
        IAppConfigRepository configRepository,
        LocalizationService loc,
        ILogger<UpdateHandler> logger)
    {
        _botClient = botClient;
        _userRepository = userRepository;
        _configRepository = configRepository;
        _loc = loc;
        _logger = logger;
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (update.Type == UpdateType.Message && update.Message?.Text != null)
            {
                await HandleMessageAsync(update.Message, cancellationToken);
            }
            else if (update.Type == UpdateType.CallbackQuery)
            {
                await HandleCallbackQueryAsync(update.CallbackQuery!, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update");
        }
    }

    private async Task HandleMessageAsync(Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var text = message.Text!;
        var user = await _userRepository.GetByChatIdAsync(chatId);

        // Ensure user exists
        if (user == null)
        {
            user = new BotUser
            {
                ChatId = chatId,
                Username = message.Chat.Username,
                FullName = $"{message.Chat.FirstName} {message.Chat.LastName}".Trim(),
                Role = UserRole.User,
                Status = UserStatus.Pending,
                LanguageCode = null // Not set yet
            };
            await _userRepository.AddAsync(user);
        }

        // Language Selection Flow
        if (string.IsNullOrEmpty(user.LanguageCode))
        {
             var langButtons = new InlineKeyboardMarkup(new[]
             {
                 new [] 
                 { 
                     InlineKeyboardButton.WithCallbackData("O'zbek 🇺🇿", "lang_uz"),
                     InlineKeyboardButton.WithCallbackData("Русский 🇷🇺", "lang_ru"),
                     InlineKeyboardButton.WithCallbackData("English 🇺🇸", "lang_en")
                 }
             });
             await _botClient.SendMessage(chatId, "Welcome! Please select your language / Iltimos tilni tanlang / Пожалуйста, выберите язык:", replyMarkup: langButtons, cancellationToken: ct);
             return;
        }

        var lang = user.LanguageCode;

        // State Handling
        if (_userStates.TryGetValue(chatId, out var state))
        {
            if (state == "SET_ADMIN_PASSWORD")
            {
                await _configRepository.SetValueAsync("AdminPassword", text);
                user.Role = UserRole.Admin;
                user.Status = UserStatus.Active;
                await _userRepository.UpdateAsync(user);
                _userStates.Remove(chatId);
                await _botClient.SendMessage(chatId, _loc.Get("PasswordSet", lang), cancellationToken: ct);
                return;
            }
            else if (state == "CHANGE_PASSWORD")
            {
                await _configRepository.SetValueAsync("AdminPassword", text);
                _userStates.Remove(chatId);
                 await _botClient.SendMessage(chatId, _loc.Get("PassChanged", lang), cancellationToken: ct);
                return;
            }
        }

        if (text == "/start")
        {
            if (user.Role == UserRole.User)
            {
                await _botClient.SendMessage(chatId, _loc.Get("WaitAdmin", lang), cancellationToken: ct);
            }
            else
            {
               await _botClient.SendMessage(chatId, _loc.Get("WelcomeBack", lang, user.Role), cancellationToken: ct);
            }
            return;
        }

        if (text == "/admin")
        {
            // Check if Password is Set (Global)
            var password = await _configRepository.GetValueAsync("AdminPassword");
            _logger.LogInformation("Admin Password Check: {Result}", string.IsNullOrEmpty(password) ? "Not Set" : "Found");
            
            if (string.IsNullOrEmpty(password))
            {
                // First time setup
                _userStates[chatId] = "SET_ADMIN_PASSWORD";
                await _botClient.SendMessage(chatId, _loc.Get("EnterPassword", lang), cancellationToken: ct);
                return;
            }

            // Check Access
            if (user.Role == UserRole.Admin || user.Role == UserRole.Deputy)
            {
                await ShowAdminPanel(chatId, user.LanguageCode, ct);
            }
            else
            {
                await _botClient.SendMessage(chatId, _loc.Get("AccessDenied", lang), cancellationToken: ct);
            }
        }
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery query, CancellationToken ct)
    {
        var chatId = query.Message!.Chat.Id;
        var data = query.Data!;
        var user = await _userRepository.GetByChatIdAsync(chatId);

        if (user == null) return;

        // Language Callback
        if (data.StartsWith("lang_"))
        {
            var selectedLang = data.Split('_')[1]; // uz, ru, en
            user.LanguageCode = selectedLang;
            await _userRepository.UpdateAsync(user);
            await _botClient.SendMessage(chatId, _loc.Get("LanguageSelected", selectedLang), cancellationToken: ct);
            
            // Continue flow
            // If admin password is not set, prompt it just in case he is the first user
            var pass = await _configRepository.GetValueAsync("AdminPassword");
            if (string.IsNullOrEmpty(pass))
            {
                 await _botClient.SendMessage(chatId, "Type /admin to set up the system password.", cancellationToken: ct);
            }
            else
            {
                 // Default message
                 if (user.Role == UserRole.User)
                    await _botClient.SendMessage(chatId, _loc.Get("WaitAdmin", selectedLang), cancellationToken: ct);
            }

            await _botClient.AnswerCallbackQuery(query.Id, cancellationToken: ct);
            return;
        }

        // Ensure user has language set before proceeding with other actions (though rare here)
        var lang = user.LanguageCode ?? "uz"; 

        if (user.Role != UserRole.Admin && user.Role != UserRole.Deputy)
        {
            await _botClient.AnswerCallbackQuery(query.Id, "Unauthorized", cancellationToken: ct);
            return;
        }

        if (data == "admin_show_waiting")
        {
            var pendingUsers = await _userRepository.GetPendingUsersAsync();
            if (pendingUsers.Count == 0)
            {
                 await _botClient.SendMessage(chatId, _loc.Get("NoPending", lang), cancellationToken: ct);
                 return;
            }
            
            var buttons = pendingUsers.Select(u => new [] 
            { 
                InlineKeyboardButton.WithCallbackData($"{u.FullName} (@{u.Username})", $"user_select_{u.ChatId}") 
            }).ToList();

            await _botClient.SendMessage(chatId, _loc.Get("SelectUser", lang), 
                replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
        }
        else if (data.StartsWith("user_select_"))
        {
            var targetId = long.Parse(data.Substring("user_select_".Length));
            // Show Actions for this user
            var buttons = new List<InlineKeyboardButton[]>();
            
            buttons.Add(new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Action_Storekeeper", lang), $"set_role_{targetId}_storekeeper") });

            if (user.Role == UserRole.Admin)
            {
                 buttons.Insert(0, new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Action_Deputy", lang), $"set_role_{targetId}_deputy") });
                 buttons.Add(new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Action_Admin", lang), $"set_role_{targetId}_admin") });
            }

            await _botClient.SendMessage(chatId, _loc.Get("SelectRole", lang, targetId), replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
        }
        else if (data.StartsWith("set_role_"))
        {
            var parts = data.Split('_');
            var targetId = long.Parse(parts[2]);
            var roleName = parts[3];

            var targetUser = await _userRepository.GetByChatIdAsync(targetId);
            if (targetUser != null)
            {
                switch (roleName)
                {
                    case "deputy": targetUser.Role = UserRole.Deputy; break;
                    case "storekeeper": targetUser.Role = UserRole.Storekeeper; break;
                    case "admin": targetUser.Role = UserRole.Admin; break;
                }
                targetUser.Status = UserStatus.Active;
                await _userRepository.UpdateAsync(targetUser);

                await _botClient.SendMessage(chatId, _loc.Get("RoleUpdated", lang, roleName), cancellationToken: ct);
                try {
                    var targetLang = targetUser.LanguageCode ?? "uz";
                    await _botClient.SendMessage(targetId, _loc.Get("YourRoleUpdated", targetLang, roleName), cancellationToken: ct);
                } catch {} 
            }
        }
        else if (data == "admin_change_pass")
        {
             _userStates[chatId] = "CHANGE_PASSWORD";
             await _botClient.SendMessage(chatId, _loc.Get("ChangePass", lang), cancellationToken: ct);
        }

        await _botClient.AnswerCallbackQuery(query.Id, cancellationToken: ct);
    }

    private async Task ShowAdminPanel(long chatId, string lang, CancellationToken ct)
    {
        var pendingCount = (await _userRepository.GetPendingUsersAsync()).Count;

        var buttons = new List<InlineKeyboardButton[]>
        {
            new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Notifications", lang, pendingCount), "admin_show_waiting") },
            new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_ChangePass", lang), "admin_change_pass") }
        };

        await _botClient.SendMessage(chatId, "Admin Panel:", replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }
}
