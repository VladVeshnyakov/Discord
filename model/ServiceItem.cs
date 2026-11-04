namespace DiscordBirge.Models;

public enum ServiceType { Boost, Calibration, LP, Behavior }

public class ServiceItem
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public ServiceType Type { get; set; }
    public int MinMmr { get; set; }
    public int MaxMmr { get; set; }
    
    public ulong BoosterId { get; set; }
    public string BoosterSteam { get; set; } 
    
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}