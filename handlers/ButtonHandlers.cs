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
        if (component.Data.CustomId == "select_service_to_buy")
        {
            var selected = component.Data.Values.FirstOrDefault();
            if (int.TryParse(selected, out int serviceId))
            {
                _actions.SaveUserSelection(component.User.Id, serviceId);
                await component.RespondAsync("✅ Услуга выбрана! Теперь нажмите «Купить».", ephemeral: true);
            }
        }
        else
        {
            await component.RespondAsync("Выбор принят.", ephemeral: true);
        }
    }

    public async Task HandleButtonAsync(SocketMessageComponent component)
    {
        if (component.Data.CustomId.StartsWith("page_"))
        {
            var parts = component.Data.CustomId.Split('_');
            if (parts.Length == 3)
            {
                string filterPart = parts[1];
                int page = int.Parse(parts[2]);

                ServiceType? filterType = filterPart switch
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

        if (component.Data.CustomId.StartsWith("start_shift_"))
        {
            var parts = component.Data.CustomId.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[2], out int startOrderId))
            {
                await _actions.StartShiftAsync(component, startOrderId);
                return;
            }
        }

        if (component.Data.CustomId.StartsWith("end_shift_"))
        {
            var parts = component.Data.CustomId.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[2], out int endOrderId))
            {
                await _actions.EndShiftAsync(component, endOrderId);
                return;
            }
        }

        switch (component.Data.CustomId)
        {
            case "create_ticket": await _actions.CreateTicketAsync(component); break;
            case "menu_history": await _actions.ShowHistoryAsync(component); break;
            case "menu_services": await _actions.ShowServicesMenuAsync(component); break;
            case "services_my": await _actions.ShowMyServicesAsync(component); break;
            case "services_add": await _actions.OpenAddServiceModalAsync(component); break;
            case "services_all_exchange": await _actions.ShowAllExchangeServicesAsync(component); break;
            case "menu_back": await _actions.ShowMainMenuAsync(component); break;
            
            case "filter_all": await _actions.FilterServicesAsync(component, null, 0); break;
            case "filter_boost": await _actions.FilterServicesAsync(component, ServiceType.Boost, 0); break;
            case "filter_calibration": await _actions.FilterServicesAsync(component, ServiceType.Calibration, 0); break;
            case "filter_lp": await _actions.FilterServicesAsync(component, ServiceType.LP, 0); break;
            
            case "buy_service_btn": await _actions.HandleBuyServiceAsync(component); break;

            default:
                await component.RespondAsync("Неизвестная кнопка.", ephemeral: true);
                break;
        }
    }
}