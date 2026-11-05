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
    
    // Итоговая сумма заказа
    public decimal Price { get; set; } 
    public decimal BasePricePerGame { get; set; } // Цена за 1 игру (для LP)

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

    public ulong? ClientChannelId { get; set; }   // ID канала клиента
    public ulong? BoosterChannelId { get; set; }  // ID канала бустера
    // --- ИГРЫ (для LP) ---
    public int TargetGames { get; set; } = 0; 

    // --- СТАТИСТИКА ---
    public int BoosterGamesPlayed { get; set; } = 0;
    public int OwnerGamesPlayed { get; set; } = 0;

    public bool IsShiftActive { get; set; } = false; 
    public DateTime? ShiftStartedAt { get; set; }
    public long? LastProcessedMatchId { get; set; } 

    // --- ФОРМУЛЫ РАСЧЕТА ---
    [NotMapped]
    public int TotalGamesPlayed => BoosterGamesPlayed + OwnerGamesPlayed;

    // Цена одной игры для выплаты (Общая сумма / Общее кол-во игр)
    [NotMapped]
    public decimal PricePerGame => TotalGamesPlayed > 0 ? Price / TotalGamesPlayed : BasePricePerGame;

    // Сколько заработал бустер
    [NotMapped]
    public decimal BoosterTotalPayout => PricePerGame * BoosterGamesPlayed;
}