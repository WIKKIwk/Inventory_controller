using InventoryBot.Application.Interfaces;
using InventoryBot.Application.Services;
using InventoryBot.Infrastructure.Persistence;
using InventoryBot.Infrastructure.Persistence.Repositories;
using InventoryBot.Worker;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

// Configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Services
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAppConfigRepository, AppConfigRepository>();
builder.Services.AddScoped<IWarehouseRepository, WarehouseRepository>();
builder.Services.AddScoped<LocalizationService>();
builder.Services.AddScoped<UpdateHandler>();

// Register BotClient as singleton or scoped? 
// UpdateHandler receives it in constructor? 
// In Worker.cs we create it manually. Ideally we register it here.
builder.Services.AddSingleton<ITelegramBotClient>(sp => 
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new TelegramBotClient(config["BotToken"]!);
});


builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Ensure DB Created with Retry Logic
using (var scope = host.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var db = services.GetRequiredService<AppDbContext>();
    
    var retryCount = 0;
    while (retryCount < 10)
    {
        try 
        {
            logger.LogInformation("Attempting to connect to database... ({Attempt}/10)", retryCount + 1);
            await db.Database.EnsureCreatedAsync();
            logger.LogInformation("Database connection established and schema ensured.");
            break;
        }
        catch (Exception ex)
        {
            retryCount++;
            logger.LogWarning(ex, "Database connection failed. Retrying in 3 seconds...");
            await Task.Delay(3000);
            
            if (retryCount == 10)
            {
                logger.LogCritical(ex, "Could not connect to database after 10 attempts. Exiting.");
                throw; // Crash so Docker restarts it, but logs will show why
            }
        }
    }
}

await host.RunAsync();
