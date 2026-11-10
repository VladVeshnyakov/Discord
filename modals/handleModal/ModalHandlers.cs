using Discord;
using Discord.WebSocket;
using DiscordBirge.Data;
using DiscordBirge.Helpers;
using DiscordBirge.Models;
using DiscordBirge.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordBirge.Handlers;

public class ModalHandlers
{
    private readonly DbService _db;
    private readonly BepusdtService _bepusdt;
    private readonly OpenDotaService _dota;
    private readonly InfoMessageService _info;

    public ModalHandlers(DbService db, BepusdtService bepusdt, OpenDotaService dota, InfoMessageService info)
    {
        _db = db;
        _bepusdt = bepusdt;
        _dota = dota;
        _info = info;
    }

    public async Task HandleModalAsync(SocketModal modal)
    {
        if (modal.Data.CustomId.StartsWith("modal_dota_id_"))
        {
            await ProcessDotaIdModalAsync(modal);
            return;
        }

        if (modal.Data.CustomId.StartsWith("modal_lp_"))
        {
            await ProcessLpGamesModalAsync(modal);
            return;
        }

        switch (modal.Data.CustomId)
        {
            case "modal_create_service":
                await ProcessCreateServiceAsync(modal);
                break;
        }
    }

    private async Task ProcessDotaIdModalAsync(SocketModal modal)
    {
        var parts = modal.Data.CustomId.Split('_');
        if (parts.Length != 4 || !int.TryParse(parts[3], out int serviceId))
        {
            await modal.RespondAsync("❌ Ошибка: неверный ID услуги.", ephemeral: true);
            return;
        }

        var idStr = modal.Data.Components.First(x => x.CustomId == "dota_id").Value.Trim();
        if (!long.TryParse(idStr, out long dotaId) || dotaId <= 0)
        {
            await modal.RespondAsync("❌ Введите корректный Dota 2 Account ID (только цифры).", ephemeral: true);
            return;
        }

        var player = await _dota.GetPlayerInfoAsync(dotaId);
        if (player?.Profile == null || string.IsNullOrEmpty(player.Profile.PersonaName))
        {
            await modal.RespondAsync(
                "❌ Не удалось найти аккаунт с таким ID. Проверьте номер.\n" +
                "*Подсказка: ваш Dota ID можно узнать на [opendota.com](https://www.opendota.com).*",
                ephemeral: true);
            return;
        }

        var service = await _db.GetServiceByIdAsync(serviceId);
        if (service == null)
        {
            await modal.RespondAsync("❌ Услуга не найдена.", ephemeral: true);
            return;
        }

        var order = await _db.CreateOrderAsync(modal.User.Id, service.BoosterId, service.Id, service.Price);
        order.DotaAccountId = dotaId;

        int startMmr = player.MmrEstimate?.Estimate
                       ?? (int?)(player.ComputedMmr)
                       ?? 0;
        order.StartMmr = startMmr;
        order.CurrentMmr = startMmr;
        order.TargetMmrDelta = service.MaxMmr - service.MinMmr;

        await _db.UpdateOrderAsync(order);

        string webhookUrl = "https://lion-kotlas.ru/api/webhooks/bepusdt";
        string redirectUrl = "https://lion-kotlas.ru";
        decimal testAmount = 1.00m;

        var payUrl = await _bepusdt.CreateInvoiceAsync(
            orderId: order.Id.ToString(),
            amount: testAmount,
            orderDesc: $"{service.Name} (ID: {dotaId})",
            webhookUrl: webhookUrl,
            redirectUrl: redirectUrl);

        // Отвечаем на модалку (закрываем её)
        await modal.DeferAsync(ephemeral: true);

        if (payUrl == null)
        {
            await _info.UpdateAsync(modal.Channel, "❌ Ошибка при создании счёта. Попробуйте позже.");
            return;
        }

        string personaName = player.Profile.PersonaName;
        string rankName = DotaRankHelper.GetRankName(player.RankTier, player.LeaderboardRank);

        var embed = new EmbedBuilder()
            .WithTitle($"💳 Счёт на оплату #{order.Id}")
            .WithDescription(
                $"**Услуга:** {service.Name}\n" +
                $"**Сумма:** {service.Price} ₽\n\n" +
                $"**🎮 Аккаунт:**\n" +
                $"Никнейм: **{personaName}**\n" +
                $"Ранг: **{rankName}**\n" +
                $"MMR (оценка): **~{startMmr}**\n\n" +
                $"Оплатите по ссылке ниже. После оплаты бот создаст приватные ветки для вас и бустера.")
            .WithColor(Color.Green)
            .WithFooter("Счёт действителен 15 минут")
            .Build();

        var builder = new ComponentBuilder()
            .WithButton("💸 Перейти к оплате", style: ButtonStyle.Link, url: payUrl);

        // Обновляем ОДИН инфо-блок в канале, а не создаём новое сообщение
        await _info.UpdateAsync(
            modal.Channel, 
            "Оплатите счёт по кнопке ниже.", 
            embed: embed, 
            components: builder.Build());
    }

    private async Task ProcessLpGamesModalAsync(SocketModal modal)
    {
        var parts = modal.Data.CustomId.Split('_');
        if (parts.Length != 3 || !int.TryParse(parts[2], out int serviceId))
        {
            await modal.RespondAsync("❌ Ошибка: неверный ID услуги.", ephemeral: true);
            return;
        }

        var gamesStr = modal.Data.Components.First(x => x.CustomId == "games_count").Value.Trim();
        if (!int.TryParse(gamesStr, out int gamesCount) || gamesCount <= 0 || gamesCount > 50)
        {
            await modal.RespondAsync("❌ Введите корректное число от 1 до 50.", ephemeral: true);
            return;
        }

        var service = await _db.GetServiceByIdAsync(serviceId);
        if (service == null || service.Type != ServiceType.LP)
        {
            await modal.RespondAsync("❌ Услуга не найдена или не является LP.", ephemeral: true);
            return;
        }

        decimal totalPrice = service.Price * gamesCount;

        var order = await _db.CreateOrderAsync(
            clientId: modal.User.Id,
            boosterId: service.BoosterId,
            serviceId: service.Id,
            price: totalPrice);

        order.TargetGames = gamesCount;
        order.BasePricePerGame = service.Price;
        await _db.UpdateOrderAsync(order);

        string webhookUrl = "https://lion-kotlas.ru/api/webhooks/bepusdt";
        string redirectUrl = "https://lion-kotlas.ru";
        decimal testAmount = 1.00m;

        var payUrl = await _bepusdt.CreateInvoiceAsync(
            orderId: order.Id.ToString(),
            amount: testAmount,
            orderDesc: $"{service.Name} ({gamesCount} игр)",
            webhookUrl: webhookUrl,
            redirectUrl: redirectUrl);

        // Отвечаем на модалку (закрываем её)
        await modal.DeferAsync(ephemeral: true);

        if (payUrl == null)
        {
            await _info.UpdateAsync(modal.Channel, "❌ Ошибка при создании счёта.");
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle($"💳 Счёт на оплату #{order.Id}")
            .WithDescription(
                $"**Услуга:** {service.Name}\n" +
                $"**Игр:** {gamesCount} × {service.Price} ₽ = **{totalPrice} ₽**\n\n" +
                $"Оплатите по ссылке ниже.")
            .WithColor(Color.Green)
            .WithFooter("Счёт действителен 15 минут")
            .Build();

        var builder = new ComponentBuilder()
            .WithButton("💸 Перейти к оплате", style: ButtonStyle.Link, url: payUrl);

        // Обновляем ОДИН инфо-блок в канале
        await _info.UpdateAsync(
            modal.Channel, 
            "Оплатите счёт по кнопке ниже.", 
            embed: embed, 
            components: builder.Build());
    }

    private async Task ProcessCreateServiceAsync(SocketModal modal)
    {
        var components = modal.Data.Components.ToList();

        string name = components.First(x => x.CustomId == "service_name").Value.Trim();
        string priceStr = components.First(x => x.CustomId == "service_price").Value.Trim();
        string typeStr = components.First(x => x.CustomId == "service_type").Value.Trim();
        string mmrRange = components.First(x => x.CustomId == "mmr_range").Value.Trim();
        string steamLink = components.First(x => x.CustomId == "steam_link").Value.Trim();

        if (!decimal.TryParse(priceStr, out decimal price) || price <= 0)
        {
            await modal.RespondAsync("❌ Цена должна быть положительным числом.", ephemeral: true);
            return;
        }

        ServiceType type;
        if (typeStr.Equals("Boost", StringComparison.OrdinalIgnoreCase)) type = ServiceType.Boost;
        else if (typeStr.Equals("Calibration", StringComparison.OrdinalIgnoreCase)) type = ServiceType.Calibration;
        else if (typeStr.Equals("LP", StringComparison.OrdinalIgnoreCase)) type = ServiceType.LP;
        else if (typeStr.Equals("Behavior", StringComparison.OrdinalIgnoreCase)) type = ServiceType.Behavior;
        else
        {
            await modal.RespondAsync("❌ Тип услуги: Boost, Calibration, LP или Behavior.", ephemeral: true);
            return;
        }

        int minMmr = 0, maxMmr = 0;
        if (type == ServiceType.Boost)
        {
            if (string.IsNullOrEmpty(mmrRange))
            {
                await modal.RespondAsync("❌ Для буста нужно указать диапазон MMR (например: 1000-3000).", ephemeral: true);
                return;
            }

            var mmrParts = mmrRange.Split('-');
            if (mmrParts.Length != 2 ||
                !int.TryParse(mmrParts[0].Trim(), out minMmr) ||
                !int.TryParse(mmrParts[1].Trim(), out maxMmr) ||
                minMmr >= maxMmr)
            {
                await modal.RespondAsync("❌ Диапазон MMR введён неверно. Пример: 1000-3000 (мин-макс).", ephemeral: true);
                return;
            }
        }

        if (!string.IsNullOrEmpty(steamLink) && !steamLink.StartsWith("https://steamcommunity.com/"))
        {
            await modal.RespondAsync("❌ Ссылка должна начинаться с https://steamcommunity.com/", ephemeral: true);
            return;
        }

        var service = new ServiceItem
        {
            Name = name,
            Price = price,
            Type = type,
            MinMmr = minMmr,
            MaxMmr = maxMmr,
            BoosterId = modal.User.Id,
            BoosterSteam = steamLink,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _db.AddServiceAsync(service);

        // Отвечаем на модалку (закрываем её)
        await modal.DeferAsync(ephemeral: true);

        var embed = new EmbedBuilder()
            .WithTitle("✅ Услуга создана")
            .WithDescription(
                $"**Название:** {service.Name}\n" +
                $"**Тип:** {service.Type}\n" +
                $"**Цена:** {service.Price} ₽" +
                (type == ServiceType.Boost ? $"\n**Диапазон MMR:** {minMmr} - {maxMmr}" : "") +
                (string.IsNullOrEmpty(steamLink) ? "" : $"\n**Steam:** [профиль]({steamLink})"))
            .WithColor(Color.Green)
            .WithFooter($"ID услуги: {service.Id}")
            .Build();

        // Показываем результат в инфо-блоке
        await _info.UpdateAsync(modal.Channel, "Услуга успешно создана.", embed: embed);
    }
}