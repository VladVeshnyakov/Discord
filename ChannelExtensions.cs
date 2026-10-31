using Discord;
using System.Threading.Tasks;
namespace DiscordBirge;

public static class ChannelExtensions
{

    public static async Task<IUserMessage> SendOrEditMessageAsync(
        this IMessageChannel channel,
        ulong? lastMessageId = null,
        string text = null,
        bool isTTS = false,
        Embed embed = null,
        RequestOptions options = null,
        ComponentBuilder components = null,
        MessageReference messageReference = null,
        AllowedMentions allowedMentions = null,
        MessageComponent messageComponent = null)
    {
        if (lastMessageId.HasValue)
        {
            var msg = await channel.GetMessageAsync(lastMessageId.Value) as IUserMessage;
            if (msg != null)
            {
                await msg.ModifyAsync(m =>
                {
                    m.Content = text ?? msg.Content;
                    m.Embed = embed != null ? new Optional<Embed>(embed) : new Optional<Embed>();
                    m.Components = components != null ? components.Build() : new ComponentBuilder().Build();
                });
                return msg;
            }
        }
        return await channel.SendMessageAsync(
            text: text,
            isTTS: isTTS,
            embed: embed,
            options: options,
            components: components?.Build(),
            messageReference: messageReference,
            allowedMentions: allowedMentions);
    }
}