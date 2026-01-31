namespace InventoryBot.Application.Services;

public class LocalizationService
{
    private static readonly Dictionary<string, Dictionary<string, string>> _resources = new()
    {
        ["uz"] = new()
        {
            ["Welcome"] = "Xush kelibsiz! Iltimos, tilni tanlang:",
            ["LanguageSelected"] = "Til tanlandi: O'zbekcha",
            ["WaitAdmin"] = "Admin sizga rol berishini kuting.",
            ["WelcomeBack"] = "Xush kelibsiz, {0}. Panel uchun /admin deb yozing.",
            ["PasswordSet"] = "Parol o'rnatildi. Siz endi Asosiy Adminsiz. Kirish uchun /admin deb yozing.",
            ["EnterPassword"] = "Tizim ishga tushmagan. Yangi Admin parolini kiriting:",
            ["AccessDenied"] = "Sizda Admin Panelga kirish huquqi yo'q.",
            ["NoPending"] = "Tasdiqlashni kutayotganlar yo'q.",
            ["SelectUser"] = "Foydalanuvchini tanlang:",
            ["SelectRole"] = "Foydalanuvchi ID {0} uchun rol tanlang:",
            ["RoleUpdated"] = "Foydalanuvchi roli {0} ga o'zgardi.",
            ["YourRoleUpdated"] = "Sizning rolingiz o'zgardi: {0}",
            ["ChangePass"] = "Yangi parolni kiriting:",
            ["PassChanged"] = "Parol muvaffaqiyatli o'zgartirildi.",
            ["Action_Deputy"] = "O'rinbosar qilish",
            ["Action_Storekeeper"] = "Omborchi qilish",
            ["Action_Admin"] = "Admin qilish",
            ["Btn_Notifications"] = "Bildirishnomalar (Kutayotganlar: {0})",
            ["Btn_ChangePass"] = "Parolni o'zgartirish"
        },
        ["ru"] = new()
        {
            ["Welcome"] = "Добро пожаловать! Пожалуйста, выберите язык:",
            ["LanguageSelected"] = "Язык выбран: Русский",
            ["WaitAdmin"] = "Подождите, пока администратор назначит вам роль.",
            ["WelcomeBack"] = "С возвращением, {0}. Введите /admin для входа в панель.",
            ["PasswordSet"] = "Пароль установлен. Вы теперь Главный Администратор. Введите /admin для доступа.",
            ["EnterPassword"] = "Система не инициализирована. Введите новый пароль администратора:",
            ["AccessDenied"] = "У вас нет доступа к Панели Администратора.",
            ["NoPending"] = "Нет пользователей, ожидающих подтверждения.",
            ["SelectUser"] = "Выберите пользователя:",
            ["SelectRole"] = "Выберите роль для пользователя ID {0}:",
            ["RoleUpdated"] = "Роль пользователя обновлена на {0}.",
            ["YourRoleUpdated"] = "Ваша роль обновлена: {0}",
            ["ChangePass"] = "Введите новый пароль:",
            ["PassChanged"] = "Пароль успешно изменен.",
            ["Action_Deputy"] = "Сделать заместителем",
            ["Action_Storekeeper"] = "Сделать кладовщиком",
            ["Action_Admin"] = "Сделать админом",
            ["Btn_Notifications"] = "Уведомления (Ожидают: {0})",
            ["Btn_ChangePass"] = "Изменить пароль"
        },
        ["en"] = new()
        {
            ["Welcome"] = "Welcome! Please select your language:",
            ["LanguageSelected"] = "Language selected: English",
            ["WaitAdmin"] = "Please wait for an admin to assign you a role.",
            ["WelcomeBack"] = "Welcome back, {0}. Type /admin for panel.",
            ["PasswordSet"] = "Password set. You are now the Main Admin. Type /admin to access the panel.",
            ["EnterPassword"] = "System not initialized. Please enter a new Admin Password:",
            ["AccessDenied"] = "You do not have permission to access the Admin Panel.",
            ["NoPending"] = "No pending users found.",
            ["SelectUser"] = "Select a user to manage:",
            ["SelectRole"] = "Select role for User ID {0}:",
            ["RoleUpdated"] = "User role updated to {0}.",
            ["YourRoleUpdated"] = "Your role has been updated to: {0}",
            ["ChangePass"] = "Enter new password:",
            ["PassChanged"] = "Password changed successfully.",
            ["Action_Deputy"] = "Make Deputy",
            ["Action_Storekeeper"] = "Make Storekeeper",
            ["Action_Admin"] = "Make Admin",
            ["Btn_Notifications"] = "Notifications (Waiting: {0})",
            ["Btn_ChangePass"] = "Change Password"
        }
    };

    public string Get(string key, string langCode, params object[] args)
    {
        // Default to 'uz' if not found
        if (string.IsNullOrEmpty(langCode)) langCode = "uz";
        
        if (!_resources.ContainsKey(langCode)) langCode = "uz";

        var dict = _resources[langCode];
        if (dict.TryGetValue(key, out var value))
        {
            return string.Format(value, args);
        }
        return key; // Return key if translation missing
    }
}
