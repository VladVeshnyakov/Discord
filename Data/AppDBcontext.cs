using DiscordBirge.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscordBirge.Data;

public class AppDbContext : DbContext
{
    // Таблицы в БД
    public DbSet<ServiceItem> Services { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<UserProfile> Users { get; set; }
    public DbSet<Shift> Shifts { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Настройка связи: Заказ ссылается на Услугу
        modelBuilder.Entity<Order>()
            .HasOne(o => o.Service)
            .WithMany()
            .HasForeignKey(o => o.ServiceId);

        // Делаем DiscordId уникальным для пользователей
        modelBuilder.Entity<UserProfile>()
            .HasIndex(u => u.DiscordId)
            .IsUnique();

        // Указываем, что цена должна храниться с точностью до копеек (2 знака)
        modelBuilder.Entity<ServiceItem>()
            .Property(s => s.Price)
            .HasPrecision(18, 2);
            
        modelBuilder.Entity<Order>()
            .Property(o => o.Price)
            .HasPrecision(18, 2);
        modelBuilder.Entity<Shift>()
            .HasOne(s => s.Order)
            .WithMany(o => o.Shifts)
            .HasForeignKey(s => s.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}