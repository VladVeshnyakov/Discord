using Discord;
using Discord.WebSocket;
using DiscordBirge.Handlers;
using DiscordBirge.Data;
using DiscordBirge.Helpers;
using DiscordBirge.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using DiscordBirge.Actions;

namespace DiscordBirge;

public class BotService
{
    private readonly DiscordSocketClient _client;
    private readonly IConfiguration _config;
    private readonly IServiceProvider _services;

    public BotService(DiscordSocketClient client, IConfiguration config, IServiceProvider services)
    {
        _client = client;
        _config = config;
        _services = services;

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;

        _client.ButtonExecuted += async component =>
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbService>();
            var bepusdt = scope.ServiceProvider.GetRequiredService<BepusdtService>();
            var orderChannels = scope.ServiceProvider.GetRequiredService<OrderChannelService>();
            var info = scope.ServiceProvider.GetRequiredService<InfoMessageService>();
            var dota = scope.ServiceProvider.GetRequiredService<OpenDotaService>();

            var actions = new UserActions(db, bepusdt, orderChannels, info, dota);
            var handlers = new ButtonHandlers(actions, info, db); // <-- Добавили db
            await handlers.HandleButtonAsync(component);
        };

        _client.SelectMenuExecuted += async component =>
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbService>();
            var bepusdt = scope.ServiceProvider.GetRequiredService<BepusdtService>();
            var orderChannels = scope.ServiceProvider.GetRequiredService<OrderChannelService>();
            var info = scope.ServiceProvider.GetRequiredService<InfoMessageService>();
            var dota = scope.ServiceProvider.GetRequiredService<OpenDotaService>();

            var actions = new UserActions(db, bepusdt, orderChannels, info, dota); 
            var handlers = new ButtonHandlers(actions, info, db);
            await handlers.HandleSelectMenuAsync(component);
        };

        _client.ModalSubmitted += async modal =>
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbService>();
            var bepusdt = scope.ServiceProvider.GetRequiredService<BepusdtService>();
            var dota = scope.ServiceProvider.GetRequiredService<OpenDotaService>();
            var info = scope.ServiceProvider.GetRequiredService<InfoMessageService>();
    
            var modalHandlers = new ModalHandlers(db, bepusdt, dota, info);
            await modalHandlers.HandleModalAsync(modal);
        };
    }

    public async Task StartAsync()
    {
        await _client.LoginAsync(TokenType.Bot, _config["Discord:Token"]);
        await _client.StartAsync();
    }

    private Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }

    private async Task ReadyAsync()
    {
        Console.WriteLine($"Бот {_client.CurrentUser.Username} подключен!");

        var info = _services.GetRequiredService<InfoMessageService>();
        ulong mainChannelId = 1568654498723930284;

        if (_client.GetChannel(mainChannelId) is ITextChannel textChannel)
        {
            var messages = await textChannel.GetMessagesAsync(50).FlattenAsync();
            var botMessage = messages
                .OfType<IUserMessage>()
                .FirstOrDefault(m => m.Author.Id == _client.CurrentUser.Id &&
                                     m.Components != null &&
                                     m.Components
                                         .OfType<ActionRowComponent>()
                                         .SelectMany(r => r.Components)
                                         .OfType<ButtonComponent>()
                                         .Any(b => b.CustomId == "create_ticket"));

            var embed = new EmbedBuilder()
                .WithTitle("💼 Биржевик")
                .WithDescription("Нажмите кнопку ниже, чтобы открыть личный кабинет.")
                .WithColor(Color.Blue)
                .Build();

            var builder = new ComponentBuilder()
                .WithButton("Создать личный кабинет", "create_ticket", ButtonStyle.Primary);

            if (botMessage != null)
            {
                await botMessage.ModifyAsync(m => { m.Embed = embed; m.Components = builder.Build(); });
                Console.WriteLine("[READY] Главное меню обновлено.");
            }
            else
            {
                await textChannel.SendMessageAsync(embed: embed, components: builder.Build());
                Console.WriteLine("[READY] Главное меню отправлено.");
            }
            
            var activeThreads = await textChannel.GetActiveThreadsAsync();
            foreach (var thread in activeThreads)
            {
                await info.CleanupAndCreateAsync(thread, "Жду действия.", markers:"📌");
            }
            
            await info.CleanupAndCreateAsync(textChannel, "Добро пожаловать! Выберите действие.", markers: ["📌"]);
        }
        else
        {
            Console.WriteLine("❌ Главный канал не найден.");
        }
    }
}