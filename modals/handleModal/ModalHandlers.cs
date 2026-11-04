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

    public ModalHandlers(DbService db, BepusdtService bepusdt)
    {
        _db = db;
        _bepusdt = bepusdt;
    }

    public async Task HandleModalAsync(SocketModal modal)
    {
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

    private async Task ProcessLpGamesModalAsync(SocketModal modal)
    {
        var parts = modal.Data.CustomId.Split('_');
        if (parts.Length != 3 || !int.TryParse(parts[2], out int serviceId))
        {
            await modal.RespondAsync("❌ Ошибка: неверный ID услуги.", ephemeral: true);
            return;
        }

        var gamesStr = modal.Data.Components.First(x => x.CustomId == "games_count").Value;
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
            price: totalPrice
        );

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
            redirectUrl: redirectUrl
        );

        if (payUrl == null)
        {
            await modal.RespondAsync("❌ Ошибка при создании счета на оплату.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("💳 Счет на оплату создан")
            .WithDescription($"**Услуга:** {service.Name}\n" +
                             $"**Игр:** {gamesCount} x {service.Price} ₽ = **{totalPrice} ₽**\n\n" +
                             $"[💸 Перейти к оплате]({payUrl})")
            .WithColor(Color.Green)
            .Build();

        await modal.RespondAsync(embed: embed, ephemeral: true);
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
            await modal.RespondAsync("❌ Ошибка: Цена должна быть положительным числом.", ephemeral: true);
            return;
        }

        int minMmr = 0, maxMmr = 0;
        if (typeStr.Equals("Boost", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(mmrRange))
        {
            var mmrParts = mmrRange.Split('-');
            if (mmrParts.Length == 2 && int.TryParse(mmrParts[0], out minMmr) && int.TryParse(mmrParts[1], out maxMmr)) { }
            else
            {
                await modal.RespondAsync("❌ Ошибка: Диапазон MMR введен неверно. Пример: 1000-3000", ephemeral: true);
                return;
            }
        }

        ServiceType type;
        if (typeStr.Equals("Boost", StringComparison.OrdinalIgnoreCase)) type = ServiceType.Boost;
        else if (typeStr.Equals("Calibration", StringComparison.OrdinalIgnoreCase)) type = ServiceType.Calibration;
        else if (typeStr.Equals("LP", StringComparison.OrdinalIgnoreCase)) type = ServiceType.LP;
        else if (typeStr.Equals("Behavior", StringComparison.OrdinalIgnoreCase)) type = ServiceType.Behavior;
        else
        {
            await modal.RespondAsync("❌ Ошибка: Тип должен быть Boost, Calibration, LP или Behavior.", ephemeral: true);
            return;
        }

        var service = new ServiceItem 
        { 
            Name = name, Price = price, Type = type, 
            MinMmr = minMmr, MaxMmr = maxMmr,
            BoosterId = modal.User.Id, BoosterSteam = steamLink
        };
        
        await _db.AddServiceAsync(service);

        await modal.RespondAsync($"✅ Услуга **{name}** сохранена в БД!", ephemeral: true);
    }
}