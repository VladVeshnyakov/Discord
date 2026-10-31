namespace DiscordBirge.Models;

public class UserProfile
{
    public int Id { get; set; }
    public ulong DiscordId { get; set; } // Уникальный ID пользователя
    public string Username { get; set; } 
    public string SteamLink { get; set; }
    public decimal Balance { get; set; } = 0; // Внутренний баланс (если нужен)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}