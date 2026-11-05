using DiscordBirge.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace DiscordBirge.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // Если услуги уже есть, ничего не делаем (чтобы не дублировать)
        if (await db.Services.AnyAsync())
        {
            Console.WriteLine("[DB] База уже содержит данные. Пропускаем сидинг.");
            return;
        }

        Console.WriteLine("[DB] База пуста. Добавляем 20 тестовых услуг...");

        string s1 = "https://steamcommunity.com/id/midgod";
        string s2 = "https://steamcommunity.com/id/carry_main";
        string s3 = "https://steamcommunity.com/id/support_life";
        string s4 = "https://steamcommunity.com/id/immortal_player";
        string s5 = "https://steamcommunity.com/profiles/76561198865166830";

        // Замени эти ID на реальные ID пользователей с твоего сервера для теста упоминаний
        ulong b1 = 1563714292086481046; 
        ulong b2 = 123456789012345678;
        ulong b3 = 90;

        var services = new[]
        {
            new ServiceItem { Name = "Буст MMR до 3000", Price = 1200m, Type = ServiceType.Boost, MinMmr = 0, MaxMmr = 3000, BoosterId = b1, BoosterSteam = s1 },
            new ServiceItem { Name = "Буст MMR 3000-4000", Price = 1800m, Type = ServiceType.Boost, MinMmr = 3000, MaxMmr = 4000, BoosterId = b2, BoosterSteam = s2 },
            new ServiceItem { Name = "Буст MMR 4000-5000", Price = 2500m, Type = ServiceType.Boost, MinMmr = 4000, MaxMmr = 5000, BoosterId = b3, BoosterSteam = s3 },
            new ServiceItem { Name = "Буст MMR 5000-6000", Price = 3800m, Type = ServiceType.Boost, MinMmr = 5000, MaxMmr = 6000, BoosterId = b1, BoosterSteam = s4 },
            new ServiceItem { Name = "Буст MMR 6000-7000", Price = 5500m, Type = ServiceType.Boost, MinMmr = 6000, MaxMmr = 7000, BoosterId = b2, BoosterSteam = s5 },
            new ServiceItem { Name = "Буст MMR 7000+", Price = 7500m, Type = ServiceType.Boost, MinMmr = 7000, MaxMmr = 10000, BoosterId = b3, BoosterSteam = s1 },
            new ServiceItem { Name = "Буст 1000 MMR (быстрый)", Price = 1000m, Type = ServiceType.Boost, MinMmr = 0, MaxMmr = 1000, BoosterId = b1, BoosterSteam = s2 },
            new ServiceItem { Name = "Калибровка 10 игр", Price = 3000m, Type = ServiceType.Calibration, MinMmr = 0, MaxMmr = 10000, BoosterId = b2, BoosterSteam = s3 },
            new ServiceItem { Name = "Калибровка 10 игр (Win 7+)", Price = 4500m, Type = ServiceType.Calibration, MinMmr = 0, MaxMmr = 10000, BoosterId = b3, BoosterSteam = s4 },
            new ServiceItem { Name = "Калибровка на 5000+ MMR", Price = 6000m, Type = ServiceType.Calibration, MinMmr = 4000, MaxMmr = 6000, BoosterId = b1, BoosterSteam = s5 },
            new ServiceItem { Name = "Калибровка на 7000+ MMR", Price = 9000m, Type = ServiceType.Calibration, MinMmr = 6000, MaxMmr = 10000, BoosterId = b2, BoosterSteam = s1 },
            new ServiceItem { Name = "Калибровка нового аккаунта", Price = 2000m, Type = ServiceType.Calibration, MinMmr = 0, MaxMmr = 3000, BoosterId = b3, BoosterSteam = s2 },
            new ServiceItem { Name = "Калибровка с бустом 5 игр", Price = 5500m, Type = ServiceType.Calibration, MinMmr = 0, MaxMmr = 8000, BoosterId = b1, BoosterSteam = s3 },
            new ServiceItem { Name = "Отыгровка 5 LP игр", Price = 500m, Type = ServiceType.LP, MinMmr = 0, MaxMmr = 10000, BoosterId = b2, BoosterSteam = s4 },
            new ServiceItem { Name = "Отыгровка 10 LP игр", Price = 900m, Type = ServiceType.LP, MinMmr = 0, MaxMmr = 10000, BoosterId = b3, BoosterSteam = s5 },
            new ServiceItem { Name = "Отыгровка 15 LP игр", Price = 1300m, Type = ServiceType.LP, MinMmr = 0, MaxMmr = 10000, BoosterId = b1, BoosterSteam = s1 },
            new ServiceItem { Name = "Срочная отыгровка 5 LP (1 час)", Price = 1000m, Type = ServiceType.LP, MinMmr = 0, MaxMmr = 10000, BoosterId = b2, BoosterSteam = s2 },
            new ServiceItem { Name = "Отыгровка LP + буст 1 ранга", Price = 2500m, Type = ServiceType.LP, MinMmr = 0, MaxMmr = 6000, BoosterId = b3, BoosterSteam = s3 },
            new ServiceItem { Name = "Буст MMR + Калибровка (пакет)", Price = 8000m, Type = ServiceType.Boost, MinMmr = 0, MaxMmr = 5000, BoosterId = b1, BoosterSteam = s4 },
            new ServiceItem { Name = "Отыгровка LP + Калибровка", Price = 3500m, Type = ServiceType.Calibration, MinMmr = 0, MaxMmr = 4000, BoosterId = b2, BoosterSteam = s5 }
        };

        await db.Services.AddRangeAsync(services);
        await db.SaveChangesAsync();

        Console.WriteLine($"[DB] Успешно добавлено {services.Length} услуг!");
    }
}