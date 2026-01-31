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
    private readonly ILogger<UpdateHandler> _logger;

    // Simple in-memory state management. For production use Distributed Cache (Redis)
    private static readonly Dictionary<long, string> _userStates = new(); 

    public UpdateHandler(
        ITelegramBotClient botClient,
        IUserRepository userRepository,
        IAppConfigRepository configRepository,
        ILogger<UpdateHandler> logger)
    {
        _botClient = botClient;
        _userRepository = userRepository;
        _configRepository = configRepository;
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
                Status = UserStatus.Pending
            };
            await _userRepository.AddAsync(user);
        }

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
                await _botClient.SendMessage(chatId, "Password set successfully. You are now the Main Admin. Type /admin to access the panel.", cancellationToken: ct);
                return;
            }
            else if (state == "CHANGE_PASSWORD")
            {
                await _configRepository.SetValueAsync("AdminPassword", text);
                _userStates.Remove(chatId);
                 await _botClient.SendMessage(chatId, "Password changed successfully.", cancellationToken: ct);
                return;
            }
        }

        if (text == "/start")
        {
            if (user.Role == UserRole.User)
            {
                await _botClient.SendMessage(chatId, "Welcome! Please wait for an admin to assign you a role.", cancellationToken: ct);
            }
            else
            {
               await _botClient.SendMessage(chatId, $"Welcome back, {user.Role}. Type /admin for panel.", cancellationToken: ct);
            }
            return;
        }

        if (text == "/admin")
        {
            // Check if Password is Set (Global)
            var password = await _configRepository.GetValueAsync("AdminPassword");
            
            if (string.IsNullOrEmpty(password))
            {
                // First time setup
                _userStates[chatId] = "SET_ADMIN_PASSWORD";
                await _botClient.SendMessage(chatId, "System not initialized. Please enter a new Admin Password:", cancellationToken: ct);
                return;
            }

            // Check Access
            if (user.Role == UserRole.Admin || user.Role == UserRole.Deputy)
            {
                await ShowAdminPanel(chatId, ct);
            }
            else
            {
                await _botClient.SendMessage(chatId, "You do not have permission to access the Admin Panel.", cancellationToken: ct);
            }
        }
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery query, CancellationToken ct)
    {
        var chatId = query.Message!.Chat.Id;
        var data = query.Data!;
        var user = await _userRepository.GetByChatIdAsync(chatId);

        if (user == null || (user.Role != UserRole.Admin && user.Role != UserRole.Deputy))
        {
            await _botClient.AnswerCallbackQuery(query.Id, "Unauthorized", cancellationToken: ct);
            return;
        }

        if (data == "admin_show_waiting")
        {
            var pendingUsers = await _userRepository.GetPendingUsersAsync();
            if (pendingUsers.Count == 0)
            {
                 await _botClient.SendMessage(chatId, "No pending users found.", cancellationToken: ct);
                 return;
            }
            
            var buttons = pendingUsers.Select(u => new [] 
            { 
                InlineKeyboardButton.WithCallbackData($"{u.FullName} (@{u.Username})", $"user_select_{u.ChatId}") 
            }).ToList();

            await _botClient.SendMessage(chatId, "Select a user to manage (Pending Users):", 
                replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
        }
        else if (data.StartsWith("user_select_"))
        {
            var targetId = long.Parse(data.Substring("user_select_".Length));
            // Show Actions for this user
            var buttons = new []
            {
                new [] { InlineKeyboardButton.WithCallbackData("Make Deputy (O'rinbosar)", $"set_role_{targetId}_deputy") },
                new [] { InlineKeyboardButton.WithCallbackData("Make Storekeeper (Omborchi)", $"set_role_{targetId}_storekeeper") },
                 new [] { InlineKeyboardButton.WithCallbackData("Make Admin", $"set_role_{targetId}_admin") } // Only Admin?
            };
            
            // Check if current user is Admin (Deputy can't make new Deputies? Prompt says "O'rinbosarlarni faqat admin belgilay oladi")
            // So if I am Deputy, I can't appoint Deputy.
            if (user.Role == UserRole.Deputy)
            {
                // Filter out restricted roles
                 buttons = new []
                {
                    new [] { InlineKeyboardButton.WithCallbackData("Make Storekeeper (Omborchi)", $"set_role_{targetId}_storekeeper") }
                };
            }

            await _botClient.SendMessage(chatId, $"Select role for User ID {targetId}:", replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
        }
        else if (data.StartsWith("set_role_"))
        {
            // set_role_{id}_{role}
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

                await _botClient.SendMessage(chatId, $"User role updated to {roleName}.", cancellationToken: ct);
                try {
                    await _botClient.SendMessage(targetId, $"Your role has been updated to: {roleName}", cancellationToken: ct);
                } catch {} // Ignore blocking
            }
        }
        else if (data == "admin_change_pass")
        {
             _userStates[chatId] = "CHANGE_PASSWORD";
             await _botClient.SendMessage(chatId, "Enter new password:", cancellationToken: ct);
        }

        await _botClient.AnswerCallbackQuery(query.Id, cancellationToken: ct);
    }

    private async Task ShowAdminPanel(long chatId, CancellationToken ct)
    {
        var pendingCount = (await _userRepository.GetPendingUsersAsync()).Count;

        var buttons = new List<InlineKeyboardButton[]>
        {
            new [] { InlineKeyboardButton.WithCallbackData($"Bildirishnomalar (Waiting: {pendingCount})", "admin_show_waiting") },
            new [] { InlineKeyboardButton.WithCallbackData("Parolni almashtirish", "admin_change_pass") }
        };

        await _botClient.SendMessage(chatId, "Admin Panel:", replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }
}
