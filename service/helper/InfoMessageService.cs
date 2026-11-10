using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordBirge.Helpers;

public class InfoMessageService
{
    private readonly DiscordSocketClient _client;
    private readonly ConcurrentDictionary<ulong, ulong> _messages = new();

    public InfoMessageService(DiscordSocketClient client)
    {
        _client = client;
    }

    public async Task CleanupAndCreateAsync(
        IMessageChannel channel,
        string text,
        Embed? embed = null,
        MessageComponent? components = null,
        string marker = "📌")
    {
        try
        {
            var messages = await channel.GetMessagesAsync(100).FlattenAsync();
            foreach (var msg in messages)
            {
                if (msg is IUserMessage userMsg &&
                    userMsg.Author.Id == _client.CurrentUser.Id &&
                    userMsg.Content?.StartsWith(marker) == true)
                {
                    try { await userMsg.DeleteAsync(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INFO CLEANUP ERROR] {ex.Message}");
        }

        _messages.TryRemove(channel.Id, out _);
        await CreateAsync(channel, text, embed, components, marker);
    }

    public async Task UpdateAsync(
        IMessageChannel channel,
        string text,
        Embed? embed = null,
        MessageComponent? components = null, // <-- ДОБАВИЛИ КОМПОНЕНТЫ
        string marker = "📌")
    {
        if (_messages.TryGetValue(channel.Id, out ulong msgId))
        {
            try
            {
                var existing = await channel.GetMessageAsync(msgId);
                if (existing is IUserMessage userMsg)
                {
                    await userMsg.ModifyAsync(m =>
                    {
                        m.Content = $"{marker} {text}";
                        m.Embed = embed;
                        // Если компоненты не переданы, очищаем старые кнопки
                        m.Components = components ?? new ComponentBuilder().Build();
                    });
                    return;
                }
            }
            catch { }
            _messages.TryRemove(channel.Id, out _);
        }

        await CreateAsync(channel, text, embed, components, marker);
    }

    private async Task CreateAsync(
        IMessageChannel channel,
        string text,
        Embed? embed,
        MessageComponent? components,
        string marker)
    {
        try
        {
            var msg = await channel.SendMessageAsync(
                $"{marker} {text}", 
                embed: embed, 
                components: components ?? new ComponentBuilder().Build()
            );
            _messages[channel.Id] = msg.Id;
            Console.WriteLine($"[INFO] Создано сообщение {msg.Id} в канале {channel.Id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INFO CREATE ERROR] {ex.Message}");
        }
    }
}