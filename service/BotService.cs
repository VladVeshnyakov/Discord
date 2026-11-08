using Discord;
using Discord.WebSocket;
using DiscordBirge.Handlers;
using DiscordBirge.Actions;
using DiscordBirge.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using DiscordBirge.Services;
using System.Linq;
using Discord.Rest; // или Discord.WebSocket, если нужно
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
            var actions = new UserActions(db, bepusdt, orderChannels);
            var handlers = new ButtonHandlers(actions);
            await handlers.HandleButtonAsync(component);
        };
        
        _client.SelectMenuExecuted += async component =>
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbService>();
            var bepusdt = scope.ServiceProvider.GetRequiredService<BepusdtService>();
            var orderChannels = scope.ServiceProvider.GetRequiredService<OrderChannelService>();
            var actions = new UserActions(db, bepusdt, orderChannels);
            var handlers = new ButtonHandlers(actions);
            await handlers.HandleSelectMenuAsync(component);
        };

        _client.ModalSubmitted += async modal => 
        {
            using var scope = _services.CreateScope();
    
            var db = scope.ServiceProvider.GetRequiredService<DbService>();
            var bepusdt = scope.ServiceProvider.GetRequiredService<BepusdtService>();
            var dota = scope.ServiceProvider.GetRequiredService<OpenDotaService>();
    
            var modalHandlers = new ModalHandlers(db, bepusdt, dota);
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
    
        ulong channelId = 1568654498723930284; 
        var channel = _client.GetChannel(channelId) as IMessageChannel;

        if (channel == null)
        {
            Console.WriteLine("ОШИБКА: Канал не найден.");
            return;
        }

        // Берем последние 10 сообщений и ищем среди них сообщение от нашего бота
        var messages = await channel.GetMessagesAsync(10).FlattenAsync();
        var botMessage = messages.FirstOrDefault(m => m.Author.Id == _client.CurrentUser.Id) as IUserMessage;

        var embed = new EmbedBuilder()
            .WithTitle("💼 Биржевик")
            .WithDescription("Нажмите кнопку ниже, чтобы открыть личный кабинет.")
            .WithColor(Color.Blue)
            .Build();

        var builder = new ComponentBuilder()
            .WithButton("Создать личный кабинет", "create_ticket", ButtonStyle.Primary);

        if (botMessage != null)
        {
            try
            {
                // Если нашли сообщение бота — просто обновляем его (спама не будет)
                await botMessage.ModifyAsync(m =>
                {
                    m.Embed = embed;
                    m.Components = builder.Build();
                });
                Console.WriteLine("Главное меню обновлено (существующее сообщение).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обновления сообщения: {ex.Message}");
            }
        }
        else
        {
            // Если сообщений бота нет (или они удалены) — создаем новое
            await channel.SendMessageAsync(embed: embed, components: builder.Build());
            Console.WriteLine("Главное меню отправлено в канал (новое сообщение).");
        }
    }
}