using Discord;
using Discord.WebSocket;
using System;
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
        Console.WriteLine($"[HELPER SafeUpdateAsync] Start. HasResponded={interaction.HasResponded}, CustomId={(interaction as SocketMessageComponent)?.Data.CustomId ?? "n/a"}");

        try
        {
            if (!interaction.HasResponded)
            {
                Console.WriteLine($"[HELPER SafeUpdateAsync] DeferAsync (ephemeral={ephemeral})");
                await interaction.DeferAsync(ephemeral);
            }

            Console.WriteLine($"[HELPER SafeUpdateAsync] ModifyOriginalResponseAsync. text='{text ?? "<null>"}', embed={(embed != null ? "yes" : "no")}, components={(components != null ? "yes" : "no")}");
            await interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = text;
                msg.Embed = embed;
                msg.Components = components;
            });
            Console.WriteLine("[HELPER SafeUpdateAsync] Done.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HELPER SafeUpdateAsync] ERROR: {ex.Message}");
        }
    }

    /// <summary>
    /// Показывает смайлик загрузки в исходном сообщении (ephemeral).
    /// </summary>
    public static async Task ShowLoadingAsync(this SocketInteraction interaction, string loadingEmoji = "Жду действия")
    {
        Console.WriteLine($"[HELPER ShowLoadingAsync] Setting loading text: {loadingEmoji}");
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
        Console.WriteLine($"[HELPER UpdateResponseAsync] Start. CustomId={(interaction as SocketMessageComponent)?.Data.CustomId ?? "n/a"}");

        try
        {
            await interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = text;
                msg.Embed = embed;
                msg.Components = components;
            });
            Console.WriteLine("[HELPER UpdateResponseAsync] Done.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HELPER UpdateResponseAsync] ERROR: {ex.Message}");
        }
    }
}