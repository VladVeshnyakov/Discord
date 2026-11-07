using Discord;
using Discord.WebSocket;
using DiscordBirge.Data;
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

    public ModalHandlers(DbService db, BepusdtService bepusdt, OpenDotaService dota)
    {
        _db = db;
        _bepusdt = bepusdt;
        _dota = dota;
    }

    public async Task HandleModalAsync(SocketModal modal)
    {
        // Dota ID (для буста/калибровки)
        if (modal.Data.CustomId.StartsWith("modal_dota_id_"))
        {
            await ProcessDotaIdModalAsync(modal);
            return;
        }

        // Количество игр LP
        if (modal.Data.CustomId.StartsWith("modal_lp_"))
        {
            await ProcessLpGamesModalAsync(modal);
            return;
        }

        // Создание услуги
        switch (modal.Data.CustomId)
        {
            case "modal_create_service":
                await ProcessCreateServiceAsync(modal);
                break;
        }
    }

    // ==========================================
    // 1. DOTA ID (буст/калибровка)
    // ==========================================
    private async Task ProcessDotaIdModalAsync(SocketModal modal)
    {
        // CustomId = modal_dota_id_{serviceId}
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

        // Валидация через OpenDota — существует ли аккаунт
        var player = await _dota.GetPlayerInfoAsync(dotaId);
        if (player?.Profile == null || string.IsNullOrEmpty(player.Profile.PersonaName))
        {
            await modal.RespondAsync("❌ Не удалось найти аккаунт с таким ID.", ephemeral: true);
            return;
        }
        var service = await _db.GetServiceByIdAsync(serviceId);
        if (service == null)
        {
            await modal.RespondAsync("❌ Услуга не найдена.", ephemeral: true);
            return;
        }

        // Создаём заказ
        var order = await _db.CreateOrderAsync(modal.User.Id, service.BoosterId, service.Id, service.Price);
        order.DotaAccountId = dotaId;

        // Подтягиваем MMR и стартовые данные
        int startMmr = player.MmrEstimate?.Estimate ?? 0;
        order.StartMmr = startMmr;
        order.CurrentMmr = startMmr;
        order.TargetMmrDelta = service.MaxMmr - service.MinMmr; // из услуги
        // Цель будет пересчитана при старте смены бустером

        await _db.UpdateOrderAsync(order);

        // Генерируем счёт
        string webhookUrl = "https://lion-kotlas.ru/api/webhooks/bepusdt";
        string redirectUrl = "https://lion-kotlas.ru";
        decimal testAmount = 1.00m; // TODO: заменить на конвертацию руб→USDT

        var payUrl = await _bepusdt.CreateInvoiceAsync(
            orderId: order.Id.ToString(),
            amount: testAmount,
            orderDesc: $"{service.Name} (ID: {dotaId})",
            webhookUrl: webhookUrl,
            redirectUrl: redirectUrl);

        if (payUrl == null)
        {
            await modal.RespondAsync("❌ Ошибка при создании счёта. Попробуйте позже.", ephemeral: true);
            return;
        }

        // Красивая карточка
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
            .WithFooter("Счёт действителен 15 минут");

        if (!string.IsNullOrEmpty(player.AvatarFull))
            embed.WithThumbnailUrl(player.AvatarFull);

        var builder = new ComponentBuilder()
            .WithButton("💸 Перейти к оплате", style: ButtonStyle.Link, url: payUrl);

        await modal.RespondAsync(embed: embed.Build(), components: builder.Build(), ephemeral: true);
    }

    // ==========================================
    // 2. КОЛИЧЕСТВО ИГР LP
    // ==========================================
    private async Task ProcessLpGamesModalAsync(SocketModal modal)
    {
        // CustomId = modal_lp_{serviceId}
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

        if (payUrl == null)
        {
            await modal.RespondAsync("❌ Ошибка при создании счёта.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle($"💳 Счёт на оплату #{order.Id}")
            .WithDescription(
                $"**Услуга:** {service.Name}\n" +
                $"**Игр:** {gamesCount} × {service.Price} ₽ = **{totalPrice} ₽**\n\n" +
                $"Оплатите по ссылке ниже.")
            .WithColor(Color.Green)
            .WithFooter("Счёт действителен 15 минут");

        var builder = new ComponentBuilder()
            .WithButton("💸 Перейти к оплате", style: ButtonStyle.Link, url: payUrl);

        await modal.RespondAsync(embed: embed.Build(), components: builder.Build(), ephemeral: true);
    }

    // ==========================================
    // 3. СОЗДАНИЕ УСЛУГИ (бустер)
    // ==========================================
    private async Task ProcessCreateServiceAsync(SocketModal modal)
    {
        var components = modal.Data.Components.ToList();

        string name = components.First(x => x.CustomId == "service_name").Value.Trim();
        string priceStr = components.First(x => x.CustomId == "service_price").Value.Trim();
        string typeStr = components.First(x => x.CustomId == "service_type").Value.Trim();
        string mmrRange = components.First(x => x.CustomId == "mmr_range").Value.Trim();
        string steamLink = components.First(x => x.CustomId == "steam_link").Value.Trim();

        // Парсинг цены
        if (!decimal.TryParse(priceStr, out decimal price) || price <= 0)
        {
            await modal.RespondAsync("❌ Цена должна быть положительным числом.", ephemeral: true);
            return;
        }

        // Парсинг типа услуги
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

        // Парсинг MMR (только для Boost)
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

        // Валидация Steam ссылки
        if (!string.IsNullOrEmpty(steamLink) && !steamLink.StartsWith("https://steamcommunity.com/"))
        {
            await modal.RespondAsync("❌ Ссылка должна начинаться с https://steamcommunity.com/", ephemeral: true);
            return;
        }

        // Создаём услугу
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

        var embed = new EmbedBuilder()
            .WithTitle("✅ Услуга создана")
            .WithDescription(
                $"**Название:** {service.Name}\n" +
                $"**Тип:** {service.Type}\n" +
                $"**Цена:** {service.Price} ₽" +
                (type == ServiceType.Boost ? $"\n**Диапазон MMR:** {minMmr} - {maxMmr}" : "") +
                (string.IsNullOrEmpty(steamLink) ? "" : $"\n**Steam:** [профиль]({steamLink})"))
            .WithColor(Color.Green)
            .WithFooter($"ID услуги: {service.Id}");

        await modal.RespondAsync(embed: embed.Build(), ephemeral: true);
    }
}