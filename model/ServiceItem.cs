namespace DiscordBirge.Models;

public enum ServiceType { Boost, Calibration, LP }

public class ServiceItem
{
    public int Id { get; set; } // Первичный ключ для БД
    public string Name { get; set; }
    public decimal Price { get; set; }
    public ServiceType Type { get; set; }
    public int MinMmr { get; set; }
    public int MaxMmr { get; set; }
    
    public ulong BoosterId { get; set; }
    public string BoosterSteam { get; set; } 
    
    public bool IsActive { get; set; } = true; // Чтобы можно было скрывать услуги, не удаляя их
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}