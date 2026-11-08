using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace DiscordBirge.Helpers;

public class InfoMessageService
{
    private readonly ConcurrentDictionary<ulong, ulong> _messages = new();

    public async Task ShowAsync(IMessageChannel channel, string text, Embed? embed = null)
    {
        try
        {
            if (_messages.TryGetValue(channel.Id, out ulong msgId))
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
                        _messages.TryRemove(channel.Id, out _);
                    }
                }
                else
                {
                    _messages.TryRemove(channel.Id, out _);
                }
            }

            var newMsg = await channel.SendMessageAsync(text, embed: embed);
            _messages[channel.Id] = newMsg.Id;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INFO ERROR] {ex.Message}");
        }
    }
}