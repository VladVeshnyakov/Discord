using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using DiscordBirge;
using DiscordBirge.Data;
using DiscordBirge.Services;
using Microsoft.EntityFrameworkCore;

class Program
{
    static async Task Main(string[] args)
    {
        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
	    {
    		config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    		config.AddEnvironmentVariables(); // <-- ДОБАВЬ ЭТУ СТРОКУ
	    })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
                {
                    GatewayIntents = GatewayIntents.Guilds | 
                                     GatewayIntents.GuildMessages | 
                                     GatewayIntents.MessageContent | 
                                     GatewayIntents.GuildMembers
                }));
                services.AddScoped<DbService>();
                services.AddHttpClient<ScuntraService>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(context.Configuration.GetConnectionString("DefaultConnection")));
                services.AddSingleton<BotService>();
            })
            .Build();

        await host.StartAsync();
        
        using (var scope = host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.MigrateAsync(); // Применит миграции
            await DbSeeder.SeedAsync(dbContext);     // Заполнит тестовыми данными
        }


        var bot = host.Services.GetRequiredService<BotService>();
        await bot.StartAsync();
        
        await Task.Delay(-1);
    }
}
