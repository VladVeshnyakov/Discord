using Discord.WebSocket;
using System.Threading.Tasks;
using DiscordBirge.Actions;
using DiscordBirge.Models;

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
        // Если это наше меню выбора услуги
        if (component.Data.CustomId == "select_service_to_buy")
        {
            // Просто отвечаем, чтобы Discord не ругался
            await component.RespondAsync("Вы выбрали услугу! Теперь нажмите «Купить».", ephemeral: true);
        }
        else
        {
            // На всякий случай отвечаем на любое другое меню
            await component.RespondAsync("Выбор принят.", ephemeral: true);
        }
    }

    public async Task HandleButtonAsync(SocketMessageComponent component)
    {
        // Пагинация (Вперед/Назад)
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
                    _ => null // "all"
                };

                await _actions.FilterServicesAsync(component, filterType, page);
                return;
            }
        }
        

        // Стандартный свитч
        switch (component.Data.CustomId)
        {
            case "create_ticket":
                await _actions.CreateTicketAsync(component);
                break;
            case "menu_history":
                await _actions.ShowHistoryAsync(component);
                break;
            case "menu_services":
                await _actions.ShowServicesMenuAsync(component);
                break;
            case "services_my":
                await _actions.ShowMyServicesAsync(component);
                break;
            case "services_add":
                await _actions.OpenAddServiceModalAsync(component);
                break;
            case "services_all_exchange": // <--- ДОБАВИЛ ЭТОТ КЕЙС
                await _actions.ShowAllExchangeServicesAsync(component);
                break;
            case "menu_back":
                await _actions.ShowMainMenuAsync(component);
                break;
            
            // Фильтры
            case "filter_all":
                await _actions.FilterServicesAsync(component, null, 0);
                break;
            case "filter_boost":
                await _actions.FilterServicesAsync(component, ServiceType.Boost, 0);
                break;
            case "filter_calibration":
                await _actions.FilterServicesAsync(component, ServiceType.Calibration, 0);
                break;
            case "filter_lp":
                await _actions.FilterServicesAsync(component, ServiceType.LP, 0);
                break;

            // Заглушка для кнопки Купить
            case "buy_service_btn": // <--- ДОБАВИЛ ЭТОТ КЕЙС
                await _actions.HandleBuyServicePlaceholderAsync(component);
                break;

            default:
                await component.RespondAsync("Неизвестная кнопка.", ephemeral: true);
                break;
        }
    }
}