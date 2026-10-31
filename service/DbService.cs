using DiscordBirge.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordBirge.Data;

public class DbService
{
    private readonly AppDbContext _db;

    public DbService(AppDbContext db)
    {
        _db = db;
    }
    

    public async Task<ServiceItem> AddServiceAsync(ServiceItem service)
    {
        _db.Services.Add(service);
        await _db.SaveChangesAsync();
        return service;
    }

    public async Task<List<ServiceItem>> GetServicesAsync(ServiceType? filterType = null, int skip = 0, int take = 5)
    {
        var query = _db.Services.Where(s => s.IsActive).AsQueryable();

        if (filterType.HasValue)
        {
            query = query.Where(s => s.Type == filterType.Value);
        }

        return await query
            .OrderByDescending(s => s.CreatedAt) // Сначала новые
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }
    // В DbService.cs
    public async Task<Order> CreateOrderAsync(ulong clientId, ulong boosterId, int serviceId, decimal price)
    {
        var order = new Order
        {
            ClientId = clientId,
            BoosterId = boosterId,
            ServiceId = serviceId,
            Price = price,
            Status = OrderStatus.Pending, // Ожидает оплаты
            CreatedAt = DateTime.UtcNow
        };
    
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    public async Task<Order?> GetOrderByCustomIdAsync(string customId)
    {
        // Мы будем использовать Id заказа в качестве order_id для Scuntra
        if (int.TryParse(customId, out int id))
            return await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);
        return null;
    }

    public async Task UpdateOrderStatusAsync(int orderId, OrderStatus status)
    {
        var order = await _db.Orders.FindAsync(orderId);
        if (order != null)
        {
            order.Status = status;
            if (status == OrderStatus.Paid) order.PaidAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
    public async Task<int> GetServicesCountAsync(ServiceType? filterType = null)
    {
        var query = _db.Services.Where(s => s.IsActive);
        
        if (filterType.HasValue)
            query = query.Where(s => s.Type == filterType.Value);
            
        return await query.CountAsync();
    }

    public async Task<ServiceItem?> GetServiceByIdAsync(int id)
    {
        return await _db.Services.FirstOrDefaultAsync(s => s.Id == id);
    }
    

    public async Task<Order> CreateOrderAsync(Order order)
    {
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    public async Task<Order?> GetOrderByIdAsync(int id)
    {
        return await _db.Orders.Include(o => o.Service).FirstOrDefaultAsync(o => o.Id == id);
    }



    public async Task<UserProfile> GetOrCreateUserAsync(ulong discordId, string username)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.DiscordId == discordId);
        if (user == null)
        {
            user = new UserProfile { DiscordId = discordId, Username = username };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
        }
        return user;
    }
}