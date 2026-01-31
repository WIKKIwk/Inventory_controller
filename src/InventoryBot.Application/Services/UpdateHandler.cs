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
    private readonly IWarehouseRepository _warehouseRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly LocalizationService _loc;
    private readonly ILogger<UpdateHandler> _logger;

    private static readonly Dictionary<long, string> _userStates = new(); // State
    private static readonly Dictionary<long, long> _pendingUserRoleAssignment = new(); // AdminChatId -> TargetUserId
    private static readonly Dictionary<long, int> _adminPanelMessageIds = new();

    public UpdateHandler(
        ITelegramBotClient botClient,
        IUserRepository userRepository,
        IAppConfigRepository configRepository,
        IWarehouseRepository warehouseRepository,
        ICustomerRepository customerRepository,
        LocalizationService loc,
        ILogger<UpdateHandler> logger)
    {
        _botClient = botClient;
        _userRepository = userRepository;
        _configRepository = configRepository;
        _warehouseRepository = warehouseRepository;
        _customerRepository = customerRepository;
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
                LanguageCode = null 
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
            else if (state == "ADD_WAREHOUSE")
            {
                // Compact Mode: Delete user input
                try { await _botClient.DeleteMessage(chatId, message.MessageId, cancellationToken: ct); } catch {}

                // Check for duplicate warehouse name
                var existing = await _warehouseRepository.GetByNameAsync(text);
                if (existing != null)
                {
                    _userStates.Remove(chatId);
                    if (_adminPanelMessageIds.TryGetValue(chatId, out var msgId))
                    {
                        await ShowAdminPanel(chatId, lang, ct, messageIdToEdit: msgId, statusMessage: _loc.Get("WarehouseDuplicate", lang, text));
                        _adminPanelMessageIds.Remove(chatId);
                    }
                    else
                    {
                        await _botClient.SendMessage(chatId, _loc.Get("WarehouseDuplicate", lang, text), cancellationToken: ct);
                        await ShowAdminPanel(chatId, lang, ct);
                    }
                    return;
                }

                var warehouse = new Warehouse { Name = text };
                await _warehouseRepository.AddAsync(warehouse);
                _userStates.Remove(chatId);
                
                // Edit Admin Panel Back
                if (_adminPanelMessageIds.TryGetValue(chatId, out var msgId2))
                {
                    await ShowAdminPanel(chatId, lang, ct, messageIdToEdit: msgId2, statusMessage: _loc.Get("WarehouseAdded", lang, text));
                    _adminPanelMessageIds.Remove(chatId);
                }
                else
                {
                    await _botClient.SendMessage(chatId, _loc.Get("WarehouseAdded", lang, text), cancellationToken: ct);
                    await ShowAdminPanel(chatId, lang, ct);
                }
                return;
            }
            else if (state == "ADD_CUSTOMER")
            {
                // Compact Mode: Delete user input
                try { await _botClient.DeleteMessage(chatId, message.MessageId, cancellationToken: ct); } catch {}

                // Check for duplicate customer name
                var existing = await _customerRepository.GetByNameAsync(text);
                if (existing != null)
                {
                    _userStates.Remove(chatId);
                    if (_adminPanelMessageIds.TryGetValue(chatId, out var msgId))
                    {
                        await ShowAdminPanel(chatId, lang, ct, messageIdToEdit: msgId, statusMessage: _loc.Get("CustomerDuplicate", lang, text));
                        _adminPanelMessageIds.Remove(chatId);
                    }
                    else
                    {
                        await _botClient.SendMessage(chatId, _loc.Get("CustomerDuplicate", lang, text), cancellationToken: ct);
                        await ShowAdminPanel(chatId, lang, ct);
                    }
                    return;
                }

                var customer = new Customer { Name = text };
                await _customerRepository.AddAsync(customer);
                _userStates.Remove(chatId);
                
                // Edit Admin Panel Back
                if (_adminPanelMessageIds.TryGetValue(chatId, out var msgId3))
                {
                    await ShowAdminPanel(chatId, lang, ct, messageIdToEdit: msgId3, statusMessage: _loc.Get("CustomerAdded", lang, text));
                    _adminPanelMessageIds.Remove(chatId);
                }
                else
                {
                    await _botClient.SendMessage(chatId, _loc.Get("CustomerAdded", lang, text), cancellationToken: ct);
                    await ShowAdminPanel(chatId, lang, ct);
                }
                return;
            }
        }

        if (text == "/start")
        {
            try { await _botClient.DeleteMessage(chatId, message.MessageId, cancellationToken: ct); } catch {}
            
            if (user.Role == UserRole.User)
            {
                 // If password not set, this might be the first user
                 var pass = await _configRepository.GetValueAsync("AdminPassword");
                 if (string.IsNullOrEmpty(pass))
                 {
                     if (!string.IsNullOrEmpty(user.LanguageCode))
                     {
                         _userStates[chatId] = "SET_ADMIN_PASSWORD";
                         await _botClient.SendMessage(chatId, _loc.Get("EnterPassword", user.LanguageCode), cancellationToken: ct);
                         return;
                     }
                 }
                 else
                 {
                    // Normal user waiting
                    if (!string.IsNullOrEmpty(user.LanguageCode))
                    {
                        await _botClient.SendMessage(chatId, _loc.Get("WaitAdmin", user.LanguageCode), cancellationToken: ct);
                        return;
                    }
                 }
            }
            else
            {
               if (!string.IsNullOrEmpty(user.LanguageCode))
                    await ShowAdminPanel(chatId, user.LanguageCode, ct); // Show Panel directly on start for admins
            }
            return;
        }

        if (text == "/admin")
        {
            try { await _botClient.DeleteMessage(chatId, message.MessageId, cancellationToken: ct); } catch {}

            // Check if Password is Set (Global)
            var password = await _configRepository.GetValueAsync("AdminPassword");
            
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
                await ShowAdminPanel(chatId, user.LanguageCode!, ct);
            }
            else
            {
                await _botClient.SendMessage(chatId, _loc.Get("AccessDenied", lang), cancellationToken: ct);
            }
            return;
        }

        // Language Selection Flow (Moved after /start checks to handle 'text' logic first if needed, but actually it was better before)
        // Let's keep original structure but add delete logic.
        
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
                await ShowAdminPanel(chatId, user.LanguageCode!, ct);
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

            // Delete the "Select Language" message
            try { await _botClient.DeleteMessageAsync(chatId, query.Message.MessageId, cancellationToken: ct); } catch {}
            
            // Check Admin Password
            var pass = await _configRepository.GetValueAsync("AdminPassword");
            if (string.IsNullOrEmpty(pass))
            {
                 // First user/setup flow - Prompt immediately
                 _userStates[chatId] = "SET_ADMIN_PASSWORD";
                 await _botClient.SendMessage(chatId, _loc.Get("EnterPassword", selectedLang), cancellationToken: ct);
            }
            else
            {
                 // Normal User Flow
                 if (user.Role == UserRole.User)
                 {
                    await _botClient.SendMessage(chatId, _loc.Get("WaitAdmin", selectedLang), cancellationToken: ct);
                 }
                 else
                 {
                     await ShowAdminPanel(chatId, selectedLang, ct);
                 }
            }

            await _botClient.AnswerCallbackQuery(query.Id, cancellationToken: ct);
            return;
        }

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
                 await _botClient.AnswerCallbackQuery(query.Id, _loc.Get("NoPending", lang), cancellationToken: ct);
                 return;
            }
            
            var buttons = pendingUsers.Select(u => new [] 
            { 
                InlineKeyboardButton.WithCallbackData($"{u.FullName} (@{u.Username})", $"user_select_{u.ChatId}") 
            }).ToList();
            buttons.Add(new [] { InlineKeyboardButton.WithCallbackData("🔙", "admin_back_to_panel") });

            await _botClient.EditMessageText(chatId, query.Message.MessageId, _loc.Get("SelectUser", lang), 
                replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
        }
        else if (data == "admin_back_to_panel")
        {
             await ShowAdminPanel(chatId, lang, ct, messageIdToEdit: query.Message.MessageId);
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
            buttons.Add(new [] { InlineKeyboardButton.WithCallbackData("🔙", "admin_show_waiting") });

            await _botClient.EditMessageText(chatId, query.Message.MessageId, _loc.Get("SelectRole", lang, targetId), replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
        }
        else if (data.StartsWith("set_role_"))
        {
            // set_role_{id}_{role}
            var parts = data.Split('_');
            var targetId = long.Parse(parts[2]);
            var roleName = parts[3];

            if (roleName == "storekeeper")
            {
                // Assign to Warehouse Flow
                var warehouses = await _warehouseRepository.GetAllAsync();
                if (warehouses.Count == 0)
                {
                     await _botClient.AnswerCallbackQuery(query.Id, _loc.Get("NoWarehouses", lang), cancellationToken: ct);
                     return;
                }

                _pendingUserRoleAssignment[chatId] = targetId; // Remember who we are assigning
                
                var buttons = warehouses.Select(w => new [] 
                { 
                     InlineKeyboardButton.WithCallbackData(w.Name, $"assign_wh_{w.Id}") 
                }).ToList();
                buttons.Add(new [] { InlineKeyboardButton.WithCallbackData("🔙", $"user_select_{targetId}") });
                
                 await _botClient.EditMessageText(chatId, query.Message.MessageId, _loc.Get("SelectWarehouse", lang), replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
                 return;
            }

            // Direct assignment for Admin/Deputy
            await AssignRole(chatId, targetId, roleName, null, lang, ct);
        }
        else if (data.StartsWith("assign_wh_"))
        {
            var warehouseId = int.Parse(data.Split('_')[2]);
            if (_pendingUserRoleAssignment.TryGetValue(chatId, out var targetId))
            {
                await AssignRole(chatId, targetId, "storekeeper", warehouseId, lang, ct);
                _pendingUserRoleAssignment.Remove(chatId);
            }
        }
        else if (data == "admin_change_pass")
        {
             _userStates[chatId] = "CHANGE_PASSWORD";
             await _botClient.SendMessage(chatId, _loc.Get("ChangePass", lang), cancellationToken: ct);
             // Password change usually requires text input so we can't edit in-place easily 
             // unless we make password change inline too, but let's focus on Warehouse first.
        }
        else if (data == "admin_add_warehouse")
        {
            if (user.Role != UserRole.Admin) // Only Admin can add warehouse? Assuming yes for now.
            {
                 await _botClient.AnswerCallbackQuery(query.Id, "Restricted", cancellationToken: ct);
                 return;
            }
            _userStates[chatId] = "ADD_WAREHOUSE";
            _adminPanelMessageIds[chatId] = query.Message.MessageId;

            var cancelBtn = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("🔙", "admin_cancel_add"));
            await _botClient.EditMessageText(chatId, query.Message.MessageId, _loc.Get("EnterWarehouseName", lang), replyMarkup: cancelBtn, cancellationToken: ct);
        }
        else if (data == "admin_cancel_add")
        {
            _userStates.Remove(chatId);
            _adminPanelMessageIds.Remove(chatId);
            await ShowAdminPanel(chatId, lang, ct, messageIdToEdit: query.Message.MessageId);
        }
        else if (data == "admin_warehouse_list")
        {
            var warehouses = await _warehouseRepository.GetAllAsync();
            if (warehouses.Count == 0)
            {
                await _botClient.AnswerCallbackQuery(query.Id, _loc.Get("NoWarehouses", lang), cancellationToken: ct);
                return;
            }

            var warehouseList = string.Join("\n", warehouses.Select((w, i) => $"{i + 1}. {w.Name}"));
            var message = $"{_loc.Get("WarehouseListTitle", lang)}\n\n{warehouseList}";
            
            var backButton = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("🔙", "admin_back_to_panel"));
            await _botClient.EditMessageText(chatId, query.Message.MessageId, message, replyMarkup: backButton, cancellationToken: ct);
        }
        else if (data == "admin_add_customer")
        {
             // Check permissions if needed (Admin only)
             var userRole = (await _userRepository.GetByChatIdAsync(chatId))?.Role;
             if (userRole != UserRole.Admin)
             {
                  await _botClient.AnswerCallbackQuery(query.Id, "Restricted", cancellationToken: ct);
                  return;
             }
             _userStates[chatId] = "ADD_CUSTOMER";
             _adminPanelMessageIds[chatId] = query.Message.MessageId;

             var cancelBtn = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("🔙", "admin_cancel_add"));
             await _botClient.EditMessageText(chatId, query.Message.MessageId, _loc.Get("EnterCustomerName", lang), replyMarkup: cancelBtn, cancellationToken: ct);
        }
        else if (data == "admin_customer_list")
        {
            var customers = await _customerRepository.GetAllAsync();
            if (customers.Count == 0)
            {
                await _botClient.AnswerCallbackQuery(query.Id, _loc.Get("NoCustomers", lang), cancellationToken: ct);
                return;
            }

            var customerList = string.Join("\n", customers.Select((c, i) => $"{i + 1}. {c.Name}"));
            var message = $"{_loc.Get("CustomerListTitle", lang)}\n\n{customerList}";
            
            var backButton = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("🔙", "admin_back_to_panel"));
            await _botClient.EditMessageText(chatId, query.Message.MessageId, message, replyMarkup: backButton, cancellationToken: ct);
        }

        await _botClient.AnswerCallbackQuery(query.Id, cancellationToken: ct);
    }
    
    private async Task AssignRole(long adminChatId, long targetUserId, string roleName, int? warehouseId, string lang, CancellationToken ct)
    {
        var targetUser = await _userRepository.GetByChatIdAsync(targetUserId);
        if (targetUser != null)
        {
            switch (roleName)
            {
                case "deputy": targetUser.Role = UserRole.Deputy; break;
                case "storekeeper": targetUser.Role = UserRole.Storekeeper; break;
                case "admin": targetUser.Role = UserRole.Admin; break;
            }
            targetUser.Status = UserStatus.Active;
            targetUser.WarehouseId = warehouseId;
            
            await _userRepository.UpdateAsync(targetUser);

            await _botClient.SendMessage(adminChatId, _loc.Get("RoleUpdated", lang, roleName), cancellationToken: ct);
            if (warehouseId.HasValue)
            {
                 var wh = await _warehouseRepository.GetByIdAsync(warehouseId.Value);
                 await _botClient.SendMessage(adminChatId, _loc.Get("UserAssignedToWarehouse", lang, wh?.Name ?? "?"), cancellationToken: ct);
            }

            try {
                var targetLang = targetUser.LanguageCode ?? "uz";
                await _botClient.SendMessage(targetUserId, _loc.Get("YourRoleUpdated", targetLang, roleName), cancellationToken: ct);
            } catch {} 
        }
    }

    private async Task ShowAdminPanel(long chatId, string lang, CancellationToken ct, int? messageIdToEdit = null, string? statusMessage = null)
    {
        var pendingCount = (await _userRepository.GetPendingUsersAsync()).Count;

        var buttons = new List<InlineKeyboardButton[]>
        {
            new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Notifications", lang, pendingCount), "admin_show_waiting") },
            new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_AddWarehouse", lang), "admin_add_warehouse") },
            new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_WarehouseList", lang), "admin_warehouse_list") },
            new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_AddCustomer", lang), "admin_add_customer") },
            new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_CustomerList", lang), "admin_customer_list") },
            new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_ChangePass", lang), "admin_change_pass") }
        };

        var text = statusMessage != null ? $"{statusMessage}\n\nAdmin Panel:" : "Admin Panel:";

        if (messageIdToEdit.HasValue)
        {
            try { await _botClient.EditMessageText(chatId, messageIdToEdit.Value, text, replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct); } catch {}
        }
        else
        {
            await _botClient.SendMessage(chatId, text, replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
        }
    }
}
