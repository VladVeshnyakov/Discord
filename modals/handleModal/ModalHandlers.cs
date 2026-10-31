using Discord;
using Discord.WebSocket;
using DiscordBirge.Data;
using DiscordBirge.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordBirge.Handlers;

public class ModalHandlers
{
    private readonly DbService _db;

    public ModalHandlers(DbService db)
    {
        _db = db;
    }

    public async Task HandleModalAsync(SocketModal modal)
    {
        switch (modal.Data.CustomId)
        {
            case "modal_create_service":
                await ProcessCreateServiceAsync(modal);
                break;
        }
    }

    private async Task ProcessCreateServiceAsync(SocketModal modal)
    {
        var components = modal.Data.Components.ToList();
        
        string name = components.First(x => x.CustomId == "service_name").Value.Trim();
        string priceStr = components.First(x => x.CustomId == "service_price").Value.Trim();
        string typeStr = components.First(x => x.CustomId == "service_type").Value.Trim();
        string mmrRange = components.First(x => x.CustomId == "mmr_range").Value.Trim();
        string steamLink = components.First(x => x.CustomId == "steam_link").Value.Trim();

        // Валидация (остается как было)
        if (!decimal.TryParse(priceStr, out decimal price) || price <= 0)
        {
            await modal.RespondAsync("❌ Ошибка: Цена должна быть положительным числом.", ephemeral: true);
            return;
        }

        var mmrParts = mmrRange.Split('-');
        if (mmrParts.Length != 2 || !int.TryParse(mmrParts[0], out int minMmr) || !int.TryParse(mmrParts[1], out int maxMmr) || minMmr >= maxMmr)
        {
            await modal.RespondAsync("❌ Ошибка: Диапазон MMR введен неверно. Пример: 1000-3000", ephemeral: true);
            return;
        }

        ServiceType type;
        if (typeStr.Equals("Boost", StringComparison.OrdinalIgnoreCase)) type = ServiceType.Boost;
        else if (typeStr.Equals("Calibration", StringComparison.OrdinalIgnoreCase)) type = ServiceType.Calibration;
        else if (typeStr.Equals("LP", StringComparison.OrdinalIgnoreCase)) type = ServiceType.LP;
        else
        {
            await modal.RespondAsync("❌ Ошибка: Тип должен быть Boost, Calibration или LP.", ephemeral: true);
            return;
        }

        // СОХРАНЯЕМ В БД ЧЕРЕЗ СЕРВИС
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