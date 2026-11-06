using Discord.WebSocket;
using System.Threading.Tasks;
using DiscordBirge.Actions;
using DiscordBirge.Models;
using System.Linq;

namespace DiscordBirge.Handlers;

public class ButtonHandlers
{
    private readonly UserActions _actions;

    public ButtonHandlers(UserActions actions)
    {
        _actions = actions;
    }

    public async Task HandleSelectMenuAsync(SocketMessageComponent component)
    {
        await component.DeferAsync(ephemeral: true);

        if (component.Data.CustomId == "select_service_to_buy")
        {
            var selected = component.Data.Values.FirstOrDefault();
            if (int.TryParse(selected, out int serviceId))
            {
                _actions.SaveUserSelection(component.User.Id, serviceId);
                await component.FollowupAsync("✅ Услуга выбрана! Теперь нажмите «Купить».", ephemeral: true);
            }
            else
            {
                await component.FollowupAsync("❌ Ошибка при выборе услуги.", ephemeral: true);
            }
        }
        else
        {
            await component.FollowupAsync("Выбор принят.", ephemeral: true);
        }
    }

    public async Task HandleButtonAsync(SocketMessageComponent component)
    {
        var id = component.Data.CustomId;

        // ==========================================
        // 1. ДИНАМИЧЕСКИЕ КНОПКИ (пагинация, смены)
        // ==========================================
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

        if (id.StartsWith("start_shift_"))
        {
            if (int.TryParse(id.Replace("start_shift_", ""), out int orderId))
            {
                await _actions.StartShiftAsync(component, orderId);
                return;
            }
        }

        if (id.StartsWith("end_shift_"))
        {
            if (int.TryParse(id.Replace("end_shift_", ""), out int orderId))
            {
                await _actions.EndShiftAsync(component, orderId);
                return;
            }
        }

        // ==========================================
        // 2. КЛИЕНТСКИЕ КНОПКИ
        // ==========================================
        switch (id)
        {
            case "buy_service_btn":
                await _actions.HandleBuyServiceAsync(component);
                return;
            case "menu_history":
                await _actions.ShowHistoryAsync(component);
                return;
        }

        // ==========================================
        // 3. КНОПКИ БУСТЕРА (ТРЕНЕРА)
        // ==========================================
        switch (id)
        {
            case "services_my":
                await _actions.ShowMyServicesAsync(component);
                return;
            case "services_add":
                await _actions.OpenAddServiceModalAsync(component);
                return;
        }

        // ==========================================
        // 4. ОБЩИЕ КНОПКИ (навигация, фильтры)
        // ==========================================
        switch (id)
        {
            case "create_ticket":
                await _actions.CreateTicketAsync(component);
                return;
            case "menu_services":
                await _actions.ShowServicesMenuAsync(component);
                return;
            case "services_all_exchange":
                await _actions.ShowAllExchangeServicesAsync(component);
                return;
            case "menu_back":
                await _actions.ShowMainMenuAsync(component);
                return;
            
            case "filter_all":
                await _actions.FilterServicesAsync(component, null, 0);
                return;
            case "filter_boost":
                await _actions.FilterServicesAsync(component, ServiceType.Boost, 0);
                return;
            case "filter_calibration":
                await _actions.FilterServicesAsync(component, ServiceType.Calibration, 0);
                return;
            case "filter_lp":
                await _actions.FilterServicesAsync(component, ServiceType.LP, 0);
                return;
        }

        // ==========================================
        // 5. НЕИЗВЕСТНАЯ КНОПКА
        // ==========================================
        await component.RespondAsync("Неизвестная кнопка.", ephemeral: true);
    }
}