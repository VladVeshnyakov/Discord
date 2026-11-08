using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace DiscordBirge.Helpers;

public class InfoMessageService
{
    // Канал → ID последнего информационного сообщения
    private readonly ConcurrentDictionary<ulong, ulong> _channelMessages = new();

    public async Task ShowAsync(IMessageChannel channel, string text, Embed? embed = null)
    {
        try
        {
            // Пытаемся отредактировать существующее сообщение
            if (_channelMessages.TryGetValue(channel.Id, out var msgId))
            {
                var existing = await channel.GetMessageAsync(msgId);
                if (existing is IUserMessage userMsg)
                {
                    try
                    {
                        await userMsg.ModifyAsync(m =>
                        {
                            m.Content = text;
                            m.Embed = embed;
                        });
                        return;
                    }
                    catch
                    {
                        // Сообщение удалили — сбрасываем запись и создадим заново
                        _channelMessages.TryRemove(channel.Id, out _);
                    }
                }
            }

            // Создаем новое
            var newMessage = await channel.SendMessageAsync(text, embed: embed);
            _channelMessages[channel.Id] = newMessage.Id;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INFO MESSAGE ERROR] {ex.Message}");
        }
    }

    public async Task ShowLoadingAsync(IMessageChannel channel, string emoji = "⏳")
    {
        await ShowAsync(channel, $"{emoji} Ожидайте...");
    }

    public async Task ClearAsync(IMessageChannel channel)
    {
        if (_channelMessages.TryRemove(channel.Id, out var msgId))
        {
            try
            {
                var existing = await channel.GetMessageAsync(msgId);
                if (existing is IUserMessage userMsg)
                    await userMsg.DeleteAsync();
            }
            catch { }
        }
    }
}