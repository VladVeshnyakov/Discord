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
    
    // Общая стоимость заказа (например, 500 руб)
    public decimal Price { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    
    public string CryptoInvoiceId { get; set; } = string.Empty;
    public string CryptoWallet { get; set; } = string.Empty;

    // --- ПОЛЯ DOTA 2 ---
    public long DotaAccountId { get; set; } 
    
    public int StartMmr { get; set; }       // MMR при создании заказа
    public int TargetMmrDelta { get; set; } // Сколько MMR нужно апнуть (например, 200)
    
    public int TargetMmr { get; set; }      // Абсолютная цель (пересчитывается при старте смены)
    public int CurrentMmr { get; set; }     // Текущий MMR аккаунта

    // Счетчики игр
    public int BoosterGamesPlayed { get; set; } = 0;
    public int OwnerGamesPlayed { get; set; } = 0;

    // Изменение MMR (для статистики)
    public int BoosterMmrChange { get; set; } = 0; 
    public int OwnerMmrChange { get; set; } = 0;

    public bool IsShiftActive { get; set; } = false; 
    public DateTime? ShiftStartedAt { get; set; }
    public long? LastProcessedMatchId { get; set; } 

    // --- ФОРМУЛЫ РАСЧЕТА ---
    
    // Общее количество игр (и бустера, и владельца)
    [NotMapped]
    public int TotalGamesPlayed => BoosterGamesPlayed + OwnerGamesPlayed;

    // Стоимость одной игры (500 руб / общее кол-во игр)
    [NotMapped]
    public decimal PricePerGame => TotalGamesPlayed > 0 ? Price / TotalGamesPlayed : Price;

    // Итоговая выплата бустеру (Цена за игру * Игры бустера)
    [NotMapped]
    public decimal BoosterTotalPayout => PricePerGame * BoosterGamesPlayed;
}