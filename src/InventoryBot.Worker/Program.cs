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

// Ensure DB Created
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Wait for DB to be ready (rudimentary retry logic could be added here for docker)
    try 
    {
        await db.Database.EnsureCreatedAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"DB Creation check failed (will retry in loop if transient): {ex.Message}");
    }
}

await host.RunAsync();
