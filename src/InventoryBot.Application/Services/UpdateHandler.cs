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
    private readonly IProductRepository _productRepository;
    private readonly LocalizationService _loc;
    private readonly ILogger<UpdateHandler> _logger;

    private static readonly Dictionary<long, string> _userStates = new(); // State
    private static readonly Dictionary<long, long> _pendingUserRoleAssignment = new(); // AdminChatId -> TargetUserId
    private static readonly Dictionary<long, int> _adminPanelMessageIds = new();
    private static readonly Dictionary<long, int> _adminPasswordPromptMessageIds = new();
    private static readonly Dictionary<long, string> _tempProductData = new(); // ChatId -> ProductName
    private static readonly HashSet<long> _authenticatedAdmins = new(); // Session-based admins

    public UpdateHandler(
        ITelegramBotClient botClient,
        IUserRepository userRepository,
        IAppConfigRepository configRepository,
        IWarehouseRepository warehouseRepository,
        ICustomerRepository customerRepository,
        IProductRepository productRepository,
        LocalizationService loc,
        ILogger<UpdateHandler> logger)
    {
        _botClient = botClient;
        _userRepository = userRepository;
        _configRepository = configRepository;
        _warehouseRepository = warehouseRepository;
        _customerRepository = customerRepository;
        _productRepository = productRepository;
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
                Roles = UserRoles.User,
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
                _authenticatedAdmins.Add(chatId);
                _userStates.Remove(chatId);
                await _botClient.SendMessage(chatId, _loc.Get("PasswordSet", lang), cancellationToken: ct);
                await ShowAdminPanel(chatId, lang, ct);
                return;
            }
            else if (state == "ENTER_ADMIN_PASSWORD")
            {
                try { await _botClient.DeleteMessage(chatId, message.MessageId, cancellationToken: ct); } catch {}

                var adminPass = await _configRepository.GetValueAsync("AdminPassword");
                if (text == adminPass)
                {
                    _authenticatedAdmins.Add(chatId);
                    _userStates.Remove(chatId);

                    int? promptMsgId = null;
                    if (_adminPasswordPromptMessageIds.TryGetValue(chatId, out var storedPromptId))
                    {
                        promptMsgId = storedPromptId;
                    }
                    else if (_adminPanelMessageIds.TryGetValue(chatId, out var existingId))
                    {
                        promptMsgId = existingId;
                    }
                    else if (message.ReplyToMessage?.From?.IsBot == true)
                    {
                        promptMsgId = message.ReplyToMessage.MessageId;
                    }

                    if (promptMsgId.HasValue)
                    {
                        await ShowAdminPanel(chatId, lang, ct, messageIdToEdit: promptMsgId.Value);
                    }
                    else
                    {
                        await ShowAdminPanel(chatId, lang, ct);
                    }

                    _adminPasswordPromptMessageIds.Remove(chatId);
                }
                else
                {
                    await _botClient.SendMessage(chatId, _loc.Get("IncorrectPassword", lang), cancellationToken: ct);
                    _userStates.Remove(chatId);
                    _adminPasswordPromptMessageIds.Remove(chatId);
                }
                return;
            }
            else if (state == "WAIT_OLD_PASSWORD")
            {
                // Compact Mode: Delete user input
                try { await _botClient.DeleteMessage(chatId, message.MessageId, cancellationToken: ct); } catch {}

                // Verify old password
                var currentPass = await _configRepository.GetValueAsync("AdminPassword");
                if (text == currentPass)
                {
                    _userStates[chatId] = "CHANGE_PASSWORD";
                    
                    // Edit Admin Panel to ask for new password
                    if (_adminPanelMessageIds.TryGetValue(chatId, out var msgId))
                    {
                         var cancelBtn = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Back", lang), "admin_back_to_panel"));
                         await _botClient.EditMessageText(chatId, msgId, _loc.Get("ChangePass", lang), replyMarkup: cancelBtn, cancellationToken: ct);
                    }
                    else
                    {
                         await _botClient.SendMessage(chatId, _loc.Get("ChangePass", lang), cancellationToken: ct);
                    }
                }
                else
                {
                    _userStates.Remove(chatId);
                    
                    // Show error in Admin Panel
                    if (_adminPanelMessageIds.TryGetValue(chatId, out var msgId))
                    {
                        await ShowAdminPanel(chatId, lang, ct, messageIdToEdit: msgId, statusMessage: _loc.Get("OldPasswordIncorrect", lang));
                        // Do not remove msgId, as we just updated it, it's still the Admin Panel
                    }
                    else
                    {
                        await ShowAdminPanel(chatId, lang, ct, statusMessage: _loc.Get("OldPasswordIncorrect", lang));
                    }
                }
                return;
            }
            else if (state == "CHANGE_PASSWORD")
            {
                // Compact Mode: Delete user input
                try { await _botClient.DeleteMessage(chatId, message.MessageId, cancellationToken: ct); } catch {}

                var currentPass = await _configRepository.GetValueAsync("AdminPassword");
                if (text == currentPass)
                {
                     // Error: New password is same as old
                     if (_adminPanelMessageIds.TryGetValue(chatId, out var errorMsgId))
                     {
                         var cancelBtn = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Back", lang), "admin_back_to_panel"));
                         await _botClient.EditMessageText(chatId, errorMsgId, _loc.Get("NewPasswordSameAsOld", lang), replyMarkup: cancelBtn, cancellationToken: ct);
                     }
                     else
                     {
                         await ShowAdminPanel(chatId, lang, ct, statusMessage: _loc.Get("NewPasswordSameAsOld", lang));
                     }
                     // State remains CHANGE_PASSWORD so they can try again
                     return;
                }

                await _configRepository.SetValueAsync("AdminPassword", text);
                _userStates.Remove(chatId);
                 
                if (_adminPanelMessageIds.TryGetValue(chatId, out var msgId))
                {
                    await ShowAdminPanel(chatId, lang, ct, messageIdToEdit: msgId, statusMessage: _loc.Get("PassChanged", lang));
                }
                else
                {
                    await ShowAdminPanel(chatId, lang, ct, statusMessage: _loc.Get("PassChanged", lang));
                }
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
                        var cancelBtn = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Back", lang), "admin_warehouses_menu"));
                        await _botClient.EditMessageText(chatId, msgId, _loc.Get("WarehouseDuplicate", lang, text), replyMarkup: cancelBtn, cancellationToken: ct);
                    }
                    else
                    {
                        var cancelBtn = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Back", lang), "admin_warehouses_menu"));
                        await _botClient.SendMessage(chatId, _loc.Get("WarehouseDuplicate", lang, text), replyMarkup: cancelBtn, cancellationToken: ct);
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
                        var cancelBtn = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Back", lang), "admin_customers_menu"));
                        await _botClient.EditMessageText(chatId, msgId, _loc.Get("WarehouseDuplicate", lang, text), replyMarkup: cancelBtn, cancellationToken: ct);
                    }
                    else
                    {
                        var cancelBtn = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Back", lang), "admin_customers_menu"));
                        await _botClient.SendMessage(chatId, _loc.Get("WarehouseDuplicate", lang, text), replyMarkup: cancelBtn, cancellationToken: ct);
                    }
                    return;
                }

                var customer = new Customer { Name = text, CreatedAt = DateTime.UtcNow };
                await _customerRepository.AddAsync(customer);

                _userStates.Remove(chatId);
                if (_adminPanelMessageIds.TryGetValue(chatId, out var msgId2))
                {
                    await ShowAdminPanel(chatId, lang, ct, messageIdToEdit: msgId2, statusMessage: _loc.Get("WarehouseSaved", lang, text));
                }
                else
                {
                    await ShowAdminPanel(chatId, lang, ct, statusMessage: _loc.Get("WarehouseSaved", lang, text));
                }
                return;
            }
            else if (state == "ADD_PRODUCT_NAME")
            {
                 // Compact Mode: Delete user input
                 try { await _botClient.DeleteMessage(chatId, message.MessageId, cancellationToken: ct); } catch {}
                 
                 _tempProductData[chatId] = text;
                 _userStates.Remove(chatId); 

                 // Show Unit Selection Buttons
                 var unitButtons = new InlineKeyboardMarkup(new[]
                 {
                     new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Unit_Kg", lang), "set_unit_0"), InlineKeyboardButton.WithCallbackData(_loc.Get("Unit_Ton", lang), "set_unit_1") },
                     new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Unit_Meter", lang), "set_unit_2"), InlineKeyboardButton.WithCallbackData(_loc.Get("Unit_Piece", lang), "set_unit_3") },
                     new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Unit_Liter", lang), "set_unit_4") }
                 });
                 
                 if (_adminPanelMessageIds.TryGetValue(chatId, out var msgId))
                 {
                     await _botClient.EditMessageText(chatId, msgId, _loc.Get("SelectUnit", lang), replyMarkup: unitButtons, cancellationToken: ct);
                 }
                 else
                 {
                     var msg = await _botClient.SendMessage(chatId, _loc.Get("SelectUnit", lang), replyMarkup: unitButtons, cancellationToken: ct);
                     _adminPanelMessageIds[chatId] = msg.MessageId;
                 }
                 return;
            }
        }

        if (text == "/start")
        {
            var roles = await EnsureRolesAsync(user);
            if (user.Status == UserStatus.Pending && !HasElevatedRole(roles))
            {
                await _botClient.SendMessage(chatId, _loc.Get("WaitAdmin", lang), cancellationToken: ct);
                return;
            }

            var displayName = string.IsNullOrWhiteSpace(user.FullName) ? (user.Username ?? "User") : user.FullName;
            var roleText = BuildRoleListText(roles, lang);
            await _botClient.SendMessage(chatId, _loc.Get("WelcomeWithRoles", lang, displayName, roleText), cancellationToken: ct);
            return;
        }
         if (text == "/admin")
        {
            try { await _botClient.DeleteMessage(chatId, message.MessageId, cancellationToken: ct); } catch {}

            var password = await _configRepository.GetValueAsync("AdminPassword");
            
            if (string.IsNullOrEmpty(password))
            {
                _userStates[chatId] = "SET_ADMIN_PASSWORD";
                await _botClient.SendMessage(chatId, _loc.Get("EnterPassword", lang), cancellationToken: ct);
                return;
            }

            // Always ask for password (ignore session)
            _userStates[chatId] = "ENTER_ADMIN_PASSWORD";
            var passwordMsg = await _botClient.SendMessage(chatId, _loc.Get("EnterExistingPassword", lang), cancellationToken: ct);
            _adminPasswordPromptMessageIds[chatId] = passwordMsg.MessageId;
            return;
        }
        else if (text == "/sklad")
        {
            // ...
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
            try { await _botClient.DeleteMessage(chatId, query.Message.MessageId, cancellationToken: ct); } catch {}
            
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
                 await _botClient.SendMessage(chatId, _loc.Get("WaitAdmin", selectedLang), cancellationToken: ct);
            }

            await _botClient.AnswerCallbackQuery(query.Id, cancellationToken: ct);
            return;
        }

        var lang = user.LanguageCode ?? "uz";
        var roles = await EnsureRolesAsync(user);

        // Check session for admin actions
        bool isSessionAdmin = _authenticatedAdmins.Contains(chatId);
        bool isDeputy = HasRole(roles, UserRoles.Deputy);

        if (data == "sklad_add_product" || data.StartsWith("set_unit_"))
        {
             // Storekeeper actions
        }
        else if (data.StartsWith("admin_") || data.StartsWith("user_select_") || data.StartsWith("set_role_"))
        {
             if (!isSessionAdmin && !isDeputy)
             {
                 await _botClient.AnswerCallbackQuery(query.Id, "Session expired. Send /admin", cancellationToken: ct);
                 return;
             }
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
             _userStates[chatId] = "WAIT_OLD_PASSWORD";
             _adminPanelMessageIds[chatId] = query.Message.MessageId;

             var cancelBtn = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Back", lang), "admin_back_to_panel"));
             await _botClient.EditMessageText(chatId, query.Message.MessageId, _loc.Get("EnterOldPassword", lang), replyMarkup: cancelBtn, cancellationToken: ct);
        }
        else if (data == "admin_close")
        {
            _userStates.Remove(chatId);
            _adminPanelMessageIds.Remove(chatId);
            try { await _botClient.DeleteMessage(chatId, query.Message.MessageId, cancellationToken: ct); } catch {}
        }
        else if (data == "admin_add_warehouse")
        {
            if (!HasRole(roles, UserRoles.Admin)) // Only Admin can add warehouse? Assuming yes for now.
            {
                 await _botClient.AnswerCallbackQuery(query.Id, "Restricted", cancellationToken: ct);
                 return;
            }
            _userStates[chatId] = "ADD_WAREHOUSE";
            _adminPanelMessageIds[chatId] = query.Message.MessageId;

            var cancelBtn = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Back", lang), "admin_warehouses_menu"));
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
            
            var backButton = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Back", lang), "admin_warehouses_menu"));
            await _botClient.EditMessageText(chatId, query.Message.MessageId, message, replyMarkup: backButton, cancellationToken: ct);
        }
        else if (data == "admin_warehouses_menu")
        {
            _userStates.Remove(chatId); // Clear any input state (like ADD_WAREHOUSE)
            var buttons = new List<InlineKeyboardButton[]>
            {
                new [] 
                { 
                    InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Add", lang), "admin_add_warehouse"),
                    InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_List", lang), "admin_warehouse_list")
                },
                new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Back", lang), "admin_back_to_panel") }
            };
            
            await _botClient.EditMessageText(chatId, query.Message.MessageId, _loc.Get("Title_ManageWarehouses", lang), replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
        }
        else if (data == "admin_add_customer")
        {
             // Check permissions if needed (Admin only)
             if (!HasRole(roles, UserRoles.Admin))
             {
                  await _botClient.AnswerCallbackQuery(query.Id, "Restricted", cancellationToken: ct);
                  return;
             }
             _userStates[chatId] = "ADD_CUSTOMER";
             _adminPanelMessageIds[chatId] = query.Message.MessageId;

             var cancelBtn = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Back", lang), "admin_customers_menu"));
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
            
            var backButton = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Back", lang), "admin_customers_menu"));
            await _botClient.EditMessageText(chatId, query.Message.MessageId, message, replyMarkup: backButton, cancellationToken: ct);
        }
        else if (data == "admin_customers_menu")
        {
            _userStates.Remove(chatId); // Clear any input state
            var buttons = new List<InlineKeyboardButton[]>
            {
                new [] 
                { 
                    InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Add", lang), "admin_add_customer"),
                    InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_List", lang), "admin_customer_list")
                },
                new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Back", lang), "admin_back_to_panel") }
            };
            
            await _botClient.EditMessageText(chatId, query.Message.MessageId, _loc.Get("Title_ManageCustomers", lang), replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
        }
        else if (data == "sklad_add_product")
        {
             _userStates[chatId] = "ADD_PRODUCT_NAME";
             await _botClient.EditMessageText(chatId, query.Message.MessageId, _loc.Get("EnterProductName", lang), cancellationToken: ct);
        }
        else if (data.StartsWith("set_unit_"))
        {
             // set_unit_0
             var unitTypeVal = int.Parse(data.Split('_')[2]);
             
             if (!_tempProductData.TryGetValue(chatId, out var productName))
             {
                 await _botClient.AnswerCallbackQuery(query.Id, "Session expired", cancellationToken: ct);
                 return;
             }
             
             if (user.WarehouseId == null) return;
             
             var product = new Product 
             {
                 Name = productName,
                 UnitType = (UnitType)unitTypeVal,
                 WarehouseId = user.WarehouseId.Value,
                 CreatedAt = DateTime.UtcNow
             };
             
             await _productRepository.AddAsync(product);
             _tempProductData.Remove(chatId);
             
             var unitKey = $"Unit_{((UnitType)unitTypeVal)}";
             var unitStr = _loc.Get(unitKey, lang);
             var savedMsg = _loc.Get("ProductSaved", lang, product.Name, unitStr);
             
             var buttons = new InlineKeyboardMarkup(new [] 
             {
                InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_AddProduct", lang), "sklad_add_product")
             });
             
             await _botClient.EditMessageText(chatId, query.Message.MessageId, savedMsg, replyMarkup: buttons, cancellationToken: ct);
        }

        await _botClient.AnswerCallbackQuery(query.Id, cancellationToken: ct);
    }

    private async Task<UserRoles> EnsureRolesAsync(BotUser user)
    {
        var roles = GetDisplayRoles(user);
        if (roles != user.Roles)
        {
            user.Roles = roles;
            await _userRepository.UpdateAsync(user);
        }

        return roles;
    }

    private static UserRoles MapLegacyRole(UserRole role)
    {
        return role switch
        {
            UserRole.Admin => UserRoles.Admin | UserRoles.User,
            UserRole.Deputy => UserRoles.Deputy | UserRoles.User,
            UserRole.Storekeeper => UserRoles.Storekeeper | UserRoles.User,
            _ => UserRoles.User
        };
    }

    private UserRoles GetDisplayRoles(BotUser user)
    {
        var roles = user.Roles == UserRoles.None ? MapLegacyRole(user.Role) : user.Roles;
        if (!HasRole(roles, UserRoles.User))
        {
            roles |= UserRoles.User;
        }
        return roles;
    }

    private static string BuildUserDisplayName(BotUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.FullName) && !string.IsNullOrWhiteSpace(user.Username))
        {
            return $"{user.FullName} (@{user.Username})";
        }
        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            return user.FullName!;
        }
        if (!string.IsNullOrWhiteSpace(user.Username))
        {
            return $"@{user.Username}";
        }
        return user.ChatId.ToString();
    }

    private static UserRoles? ParseRoleFlag(string roleName)
    {
        return roleName switch
        {
            "admin" => UserRoles.Admin,
            "deputy" => UserRoles.Deputy,
            "storekeeper" => UserRoles.Storekeeper,
            _ => null
        };
    }

    private static bool HasRole(UserRoles roles, UserRoles role)
    {
        return (roles & role) == role;
    }

    private static bool HasElevatedRole(UserRoles roles)
    {
        return HasRole(roles, UserRoles.Admin) || HasRole(roles, UserRoles.Deputy) || HasRole(roles, UserRoles.Storekeeper);
    }

    private string BuildRoleListText(UserRoles roles, string lang)
    {
        var names = new List<string>();
        if (HasRole(roles, UserRoles.Admin)) names.Add(_loc.Get("Role_Admin", lang));
        if (HasRole(roles, UserRoles.Deputy)) names.Add(_loc.Get("Role_Deputy", lang));
        if (HasRole(roles, UserRoles.Storekeeper)) names.Add(_loc.Get("Role_Storekeeper", lang));

        if (names.Count == 0)
        {
            names.Add(_loc.Get("Role_User", lang));
        }

        return string.Join(", ", names);
    }

    private InlineKeyboardMarkup BuildRoleSelectionMarkup(long targetId, bool isAdmin, string lang, string backCallback)
    {
        var buttons = new List<InlineKeyboardButton[]>();

        buttons.Add(new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Action_Storekeeper", lang), $"set_role_{targetId}_storekeeper") });

        if (isAdmin)
        {
            buttons.Insert(0, new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Action_Deputy", lang), $"set_role_{targetId}_deputy") });
            buttons.Add(new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Action_Admin", lang), $"set_role_{targetId}_admin") });
        }

        buttons.Add(new [] { InlineKeyboardButton.WithCallbackData("🔙", backCallback) });
        return new InlineKeyboardMarkup(buttons);
    }

    private string GetRoleDisplayName(UserRoles role, string lang)
    {
        return role switch
        {
            UserRoles.Admin => _loc.Get("Role_Admin", lang),
            UserRoles.Deputy => _loc.Get("Role_Deputy", lang),
            UserRoles.Storekeeper => _loc.Get("Role_Storekeeper", lang),
            _ => _loc.Get("Role_User", lang)
        };
    }

    private async Task AssignRole(long adminChatId, long targetUserId, string roleName, int? warehouseId, string lang, CancellationToken ct)
    {
        var targetUser = await _userRepository.GetByChatIdAsync(targetUserId);
        if (targetUser != null)
        {
            var roleFlag = ParseRoleFlag(roleName);
            if (roleFlag == null)
            {
                return;
            }

            var existingRoles = targetUser.Roles == UserRoles.None ? MapLegacyRole(targetUser.Role) : targetUser.Roles;
            if (!HasRole(existingRoles, UserRoles.User))
            {
                existingRoles |= UserRoles.User;
            }

            targetUser.Roles = existingRoles | roleFlag.Value;

            // Keep legacy Role in sync with latest assigned role
            switch (roleFlag.Value)
            {
                case UserRoles.Deputy: targetUser.Role = UserRole.Deputy; break;
                case UserRoles.Storekeeper: targetUser.Role = UserRole.Storekeeper; break;
                case UserRoles.Admin: targetUser.Role = UserRole.Admin; break;
            }
            targetUser.Status = UserStatus.Active;
            if (warehouseId.HasValue)
            {
                targetUser.WarehouseId = warehouseId;
            }
            
            await _userRepository.UpdateAsync(targetUser);

            var roleDisplayName = GetRoleDisplayName(roleFlag.Value, lang);
            await _botClient.SendMessage(adminChatId, _loc.Get("RoleUpdated", lang, roleDisplayName), cancellationToken: ct);
            if (warehouseId.HasValue)
            {
                 var wh = await _warehouseRepository.GetByIdAsync(warehouseId.Value);
                 await _botClient.SendMessage(adminChatId, _loc.Get("UserAssignedToWarehouse", lang, wh?.Name ?? "?"), cancellationToken: ct);
            }

            try {
                var targetLang = targetUser.LanguageCode ?? "uz";
                var targetRoleDisplay = GetRoleDisplayName(roleFlag.Value, targetLang);
                await _botClient.SendMessage(targetUserId, _loc.Get("YourRoleUpdated", targetLang, targetRoleDisplay), cancellationToken: ct);
            } catch {} 
        }
    }

    private async Task ShowAdminPanel(long chatId, string lang, CancellationToken ct, int? messageIdToEdit = null, string? statusMessage = null)
    {
        var pendingCount = (await _userRepository.GetPendingUsersAsync()).Count;

        var buttons = new List<InlineKeyboardButton[]>
        {
            new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Notifications", lang, pendingCount), "admin_show_waiting") },
            new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Menu_Warehouses", lang), "admin_warehouses_menu"), InlineKeyboardButton.WithCallbackData(_loc.Get("Menu_Customers", lang), "admin_customers_menu") },
            new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_ChangePass", lang), "admin_change_pass") },
            new [] { InlineKeyboardButton.WithCallbackData(_loc.Get("Btn_Close", lang), "admin_close") }
        };

        var text = statusMessage != null ? $"{statusMessage}\n\nAdmin Panel:" : "Admin Panel:";

        if (messageIdToEdit.HasValue)
        {
            try
            {
                await _botClient.EditMessageText(chatId, messageIdToEdit.Value, text, replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
                _adminPanelMessageIds[chatId] = messageIdToEdit.Value;
                return;
            }
            catch {}
        }

        var sent = await _botClient.SendMessage(chatId, text, replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
        _adminPanelMessageIds[chatId] = sent.MessageId;
    }

    private async Task ShowAdminPanelEdit(long chatId, int messageId, string lang, CancellationToken ct)
    {
        await ShowAdminPanel(chatId, lang, ct, messageIdToEdit: messageId);
    }
}
