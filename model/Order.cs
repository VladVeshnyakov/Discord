using System;
using System.ComponentModel.DataAnnotations.Schema;

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
    public decimal BasePricePerGame { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    
    public string CryptoInvoiceId { get; set; } = string.Empty;
    public string CryptoWallet { get; set; } = string.Empty;

    public long DotaAccountId { get; set; } 
    
    // --- MMR ---
    public int StartMmr { get; set; }       
    public int TargetMmrDelta { get; set; } 
    public int TargetMmr { get; set; }      
    public int CurrentMmr { get; set; }     
    public int BoosterMmrChange { get; set; } = 0; 
    public int OwnerMmrChange { get; set; } = 0;

    // --- ИГРЫ (для LP) ---
    public int TargetGames { get; set; } = 0; 

    // --- СТАТИСТИКА ---
    public int BoosterGamesPlayed { get; set; } = 0;
    public int OwnerGamesPlayed { get; set; } = 0;

    public bool IsShiftActive { get; set; } = false; 
    public DateTime? ShiftStartedAt { get; set; }
    public long? LastProcessedMatchId { get; set; } 

    // --- ВЕТКИ DISCORD ---
    public ulong? ClientThreadId { get; set; }
    public ulong? BoosterThreadId { get; set; }

    // --- ФОРМУЛЫ РАСЧЕТА ---
    [NotMapped]
    public int TotalGamesPlayed => BoosterGamesPlayed + OwnerGamesPlayed;

    [NotMapped]
    public decimal PricePerGame => TotalGamesPlayed > 0 ? Price / TotalGamesPlayed : BasePricePerGame;

    [NotMapped]
    public decimal BoosterTotalPayout => PricePerGame * BoosterGamesPlayed;
}