using Discord;
using Discord.WebSocket;
using DiscordBirge;
using DiscordBirge.Data;
using DiscordBirge.Services;
using Microsoft.EntityFrameworkCore;

class Program
{
    static async Task Main(string[] args)
    {
        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

        var builder = WebApplication.CreateBuilder(args);

        // 1. Добавляем поддержку контроллеров (для вебхука Scuntra)
        builder.Services.AddControllers();

        // 2. Настройка Discord-клиента
        builder.Services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds |
                             GatewayIntents.GuildMessages |
                             GatewayIntents.MessageContent |
                             GatewayIntents.GuildMembers
        }));

        // 3. Регистрация остальных сервисов
        builder.Services.AddScoped<DbService>();
        builder.Services.AddHttpClient<ScuntraService>();
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
        builder.Services.AddSingleton<BotService>();

        var app = builder.Build();

        // 4. Маппим контроллеры (чтобы API работал)
        app.MapControllers();

        // 5. Миграции и сидинг БД
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.MigrateAsync();
            await DbSeeder.SeedAsync(dbContext);
        }

        // 6. Запускаем Discord-бота
        var bot = app.Services.GetRequiredService<BotService>();
        await bot.StartAsync();

        // 7. Запускаем Web API (Kestrel)
        await app.RunAsync();
    }
}
