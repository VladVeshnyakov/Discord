namespace DiscordBirge.Models;

public enum OrderStatus { Pending, Paid, InProgress, Completed, Cancelled }

public class Order
{
    public int Id { get; set; }
    public ulong ClientId { get; set; } 
    public ulong BoosterId { get; set; } 
    public int ServiceId { get; set; }
    public ServiceItem Service { get; set; }
    
    public decimal Price { get; set; } 
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; } 
    
    public string CryptoInvoiceId { get; set; } 
    public string CryptoWallet { get; set; }
}