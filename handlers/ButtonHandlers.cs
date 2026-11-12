using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;
using DiscordBirge.Actions;
using DiscordBirge.Helpers;
using DiscordBirge.Models;
using DiscordBirge.Data; // <-- Добавили для DbService

namespace DiscordBirge.Handlers;

public class ButtonHandlers
{
    private readonly UserActions _actions;
    private readonly InfoMessageService _info;
    private readonly DbService _db; // <-- Добавили поле

    // <-- Обновили конструктор
    public ButtonHandlers(UserActions actions, InfoMessageService info, DbService db)
    {
        _actions = actions;
        _info = info;
        _db = db; 
    }

    public async Task HandleSelectMenuAsync(SocketMessageComponent component)
    {
        try
        {
            if (component.Data.CustomId == "select_service_to_buy")
            {
                var selected = component.Data.Values.FirstOrDefault();
                if (int.TryParse(selected, out int serviceId))
                {
                    _actions.SaveUserSelection(component.User.Id, serviceId);
                    await _info.UpdateAsync(component.Channel, "✅ Нажмите «Купить выбранное».   \n         Cформируется платежная страница.");
                }
            }

            await component.UpdateAsync(m => { });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SELECT MENU ERROR] {ex.Message}");
        }
    }

    public async Task HandleButtonAsync(SocketMessageComponent component)
    {
        var customId = component.Data.CustomId;
        
        if (customId.StartsWith("progress_page_"))
        {
            var parts = customId.Split('_');
            if (parts.Length == 4 && int.TryParse(parts[2], out int orderId) && int.TryParse(parts[3], out int page))
            {
                await component.DeferAsync();
                var order = await _db.GetOrderByIdAsync(orderId); // Теперь _db доступен
                if (order != null)
                {
                    await _actions.UpdateProgressAsync(component.Message.Channel, order, page);
                }
                return;
            }
        }
        
        var id = component.Data.CustomId;

        if (id.StartsWith("page_"))
        {
            var parts = id.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[2], out int page))
            {
                ServiceType? filterType = parts[1] switch
                {
                    "Boost" => ServiceType.Boost,
                    "Calibration" => ServiceType.Calibration,
                    "LP" => ServiceType.LP,
                    _ => null
                };
                await _actions.FilterServicesAsync(component, filterType, page);
                return;
            }
        }

        if (id.StartsWith("start_shift_") && int.TryParse(id.Replace("start_shift_", ""), out int startId))
        {
            await _actions.StartShiftAsync(component, startId);
            return;
        }

        if (id.StartsWith("end_shift_") && int.TryParse(id.Replace("end_shift_", ""), out int endId))
        {
            await _actions.EndShiftAsync(component, endId);
            return;
        }

        switch (id)
        {
            case "buy_service_btn": await _actions.HandleBuyServiceAsync(component); return;
            case "menu_history": await _actions.ShowHistoryAsync(component); return;
            case "services_my": await _actions.ShowMyServicesAsync(component); return;
            case "services_add": await _actions.OpenAddServiceModalAsync(component); return;
            case "create_ticket": await _actions.CreateTicketAsync(component); return;
            case "menu_services": await _actions.ShowServicesMenuAsync(component); return;
            case "services_all_exchange": await _actions.ShowAllExchangeServicesAsync(component); return;
            case "menu_back": await _actions.ShowMainMenuAsync(component); return;
            case "filter_all": await _actions.FilterServicesAsync(component, null, 0); return;
            case "filter_boost": await _actions.FilterServicesAsync(component, ServiceType.Boost, 0); return;
            case "filter_calibration": await _actions.FilterServicesAsync(component, ServiceType.Calibration, 0); return;
            case "filter_lp": await _actions.FilterServicesAsync(component, ServiceType.LP, 0); return;
        }

        try { await component.UpdateAsync(m => { }); } catch { }
    }
}