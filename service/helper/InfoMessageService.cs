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
    private readonly ConcurrentDictionary<(ulong ChannelId, string Marker), ulong> _messages = new();

    public InfoMessageService(DiscordSocketClient client)
    {
        _client = client;
    }

    // --- НОВЫЙ МЕТОД ДЛЯ ОЧИСТКИ ПРИ СТАРТЕ ---
    public async Task HardResetMarkersAsync(IMessageChannel channel, params string[] markers)
    {
        if (markers == null || markers.Length == 0) return;

        try
        {
            var messages = await channel.GetMessagesAsync(100).FlattenAsync();
            foreach (var msg in messages)
            {
                if (msg is IUserMessage userMsg &&
                    userMsg.Author.Id == _client.CurrentUser.Id &&
                    markers.Any(m => userMsg.Content?.StartsWith(m) == true))
                {
                    try { await userMsg.DeleteAsync(); } catch { }
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[HARD RESET ERROR] {ex.Message}"); }
        
        foreach (var m in markers)
        {
            _messages.TryRemove((channel.Id, m), out _);
        }
    }

    public async Task CleanupAndCreateAsync(
        IMessageChannel channel, string text, Embed? embed = null,
        MessageComponent? components = null, params string[] markers)
    {
        if (markers == null || markers.Length == 0) markers = new[] { "📌" };
        await HardResetMarkersAsync(channel, markers);
        await CreateAsync(channel, text, embed, components, markers[0]);
    }

    public async Task UpdateAsync(
        IMessageChannel channel, string text, Embed? embed = null,
        MessageComponent? components = null, params string[] markers)
    {
        if (markers == null || markers.Length == 0) markers = new[] { "📌" };

        ulong? msgId = null;
        string markerFound = null;
        
        // Ищем только в оперативной памяти
        foreach (var marker in markers)
        {
            if (_messages.TryGetValue((channel.Id, marker), out ulong id))
            {
                msgId = id;
                markerFound = marker;
                break;
            }
        }

        if (msgId.HasValue && markerFound != null)
        {
            try
            {
                var existing = await channel.GetMessageAsync(msgId.Value);
                if (existing is IUserMessage userMsg)
                {
                    await userMsg.ModifyAsync(m =>
                    {
                        m.Content = $"{markerFound} {text}";
                        m.Embed = embed;
                        m.Components = components ?? new ComponentBuilder().Build();
                    });
                    return;
                }
            }
            catch { }
            _messages.TryRemove((channel.Id, markerFound), out _);
        }
        
        // Если в памяти нет, создаем новое
        await CreateAsync(channel, text, embed, components, markers[0]);
    }

    private async Task CreateAsync(
        IMessageChannel channel, string text, Embed? embed,
        MessageComponent? components, string marker)
    {
        try
        {
            var msg = await channel.SendMessageAsync(
                $"{marker} {text}", embed: embed,
                components: components ?? new ComponentBuilder().Build());
            _messages[(channel.Id, marker)] = msg.Id;
            Console.WriteLine($"[INFO] Создано сообщение {msg.Id} в канале {channel.Id} (marker={marker})");
        }
        catch (Exception ex) { Console.WriteLine($"[INFO CREATE ERROR] {ex.Message}"); }
    }
}