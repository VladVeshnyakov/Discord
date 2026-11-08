using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

namespace DiscordBirge.Helpers;

public static class InteractionHelper
{
    /// <summary>
    /// Безопасно обновляет исходное сообщение: если ответа ещё не было — делает DeferAsync, затем ModifyOriginalResponseAsync.
    /// </summary>
    public static async Task SafeUpdateAsync(
        this SocketInteraction interaction,
        string? text = null,
        Embed? embed = null,
        MessageComponent? components = null,
        bool ephemeral = true)
    {
        if (!interaction.HasResponded)
        {
            await interaction.DeferAsync(ephemeral);
        }

        await interaction.ModifyOriginalResponseAsync(msg =>
        {
            msg.Content = text;
            msg.Embed = embed;
            msg.Components = components;
        });
    }

    /// <summary>
    /// Показывает смайлик загрузки в исходном сообщении (ephemeral).
    /// </summary>
    public static async Task ShowLoadingAsync(this SocketInteraction interaction, string loadingEmoji = "Жду действия")
    {
        await interaction.SafeUpdateAsync(loadingEmoji, ephemeral: true);
    }

    /// <summary>
    /// Обновляет исходное сообщение без проверки HasResponded (когда точно уже deferred).
    /// </summary>
    public static async Task UpdateResponseAsync(
        this SocketInteraction interaction,
        string? text = null,
        Embed? embed = null,
        MessageComponent? components = null)
    {
        await interaction.ModifyOriginalResponseAsync(msg =>
        {
            msg.Content = text;
            msg.Embed = embed;
            msg.Components = components;
        });
    }
}