using Discord;
using Discord.WebSocket;
using DiscordBirge.Data;
using DiscordBirge.Models;
using DiscordBirge.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordBirge.Actions;

public class UserActions
{
    private readonly DbService _db;
    private readonly BepusdtService _bepusdt;
    private const int ServicesPerPage = 5;

    // Хранилище: ID пользователя -> ID выбранной услуги
    private static readonly Dictionary<ulong, int> _userSelections = new();

    public UserActions(DbService db, BepusdtService bepusdt)
    {
        _db = db;
        _bepusdt = bepusdt;
    }

    public void SaveUserSelection(ulong userId, int serviceId)
    {
        _userSelections[userId] = serviceId;
    }

    // ==========================================
    // МЕНЮ И НАВИГАЦИЯ
    // ==========================================

    public async Task CreateTicketAsync(SocketMessageComponent component)
    {
        await component.DeferAsync(ephemeral: true);
        var textChannel = component.Channel as ITextChannel;
        var guildUser = component.User as IGuildUser;

        if (textChannel != null && guildUser != null)
        {
            try
            {
                var thread = await textChannel.CreateThreadAsync(
                    name: $"Заказ-{guildUser.Username}",
                    type: ThreadType.PrivateThread,
                    autoArchiveDuration: ThreadArchiveDuration.OneDay,
                    invitable: false
                );

                await thread.AddUserAsync(guildUser);
                await thread.JoinAsync();

                var embed = new EmbedBuilder()
                    .WithTitle("💼 Биржевик - Ваш личный кабинет")
                    .WithDescription("Выберите раздел:")
                    .WithColor(Color.Blue)
                    .Build();

                var builder = new ComponentBuilder()
                    .WithButton("История", "menu_history", ButtonStyle.Secondary)
                    .WithButton("Услуги", "menu_services", ButtonStyle.Primary);

                await thread.SendMessageAsync(embed: embed, components: builder.Build());
                await component.FollowupAsync($"Ветка создана! Переходите в неё: {thread.Mention}", ephemeral: true);
            }
            catch (Exception ex)
            {
                await component.FollowupAsync($"Ошибка: {ex.Message}", ephemeral: true);
            }
        }
    }

    public async Task ShowMainMenuAsync(SocketMessageComponent component)
    {
        var embed = new EmbedBuilder()
            .WithTitle("💼 Биржевик - Ваш личный кабинет")
            .WithDescription("Выберите раздел:")
            .WithColor(Color.Blue)
            .Build();

        var builder = new ComponentBuilder()
            .WithButton("История", "menu_history", ButtonStyle.Secondary)
            .WithButton("Услуги", "menu_services", ButtonStyle.Primary);

        await component.UpdateAsync(m => { m.Embed = embed; m.Components = builder.Build(); });
    }

    public async Task ShowHistoryAsync(SocketMessageComponent component)
    {
        var embed = new EmbedBuilder()
            .WithTitle("📜 История")
            .WithDescription("Здесь будет ваша история заказов. (В разработке)")
            .WithColor(Color.DarkGrey)
            .Build();
            
        var builder = new ComponentBuilder().WithButton("⬅ Назад", "menu_back", ButtonStyle.Danger);
        await component.UpdateAsync(m => { m.Embed = embed; m.Components = builder.Build(); });
    }

    public async Task ShowServicesMenuAsync(SocketMessageComponent component)
    {
        var embed = new EmbedBuilder()
            .WithTitle("🛠 Управление услугами")
            .WithDescription("Выберите действие:")
            .WithColor(Color.Green)
            .Build();

        var builder = new ComponentBuilder()
            .WithButton("Мои услуги", "services_my", ButtonStyle.Primary)
            .WithButton("Разместить услугу", "services_add", ButtonStyle.Success)
            .WithButton("Просмотр всех услуг на бирже", "services_all_exchange", ButtonStyle.Secondary)
            .WithButton("⬅ Назад", "menu_back", ButtonStyle.Danger);

        await component.UpdateAsync(m => { m.Embed = embed; m.Components = builder.Build(); });
    }

    public async Task ShowMyServicesAsync(SocketMessageComponent component)
    {
        await component.RespondAsync("📦 **Мои услуги**\nЗдесь будет список ваших услуг. (В разработке)", ephemeral: true);
    }

    // ==========================================
    // СПИСОК УСЛУГ И ПАГИНАЦИЯ
    // ==========================================

    public async Task ShowAllExchangeServicesAsync(SocketMessageComponent component)
    {
        await FilterServicesAsync(component, null, 0);
    }

    public async Task FilterServicesAsync(SocketMessageComponent component, ServiceType? filterType, int page = 0)
    {
        int totalCount = await _db.GetServicesCountAsync(filterType);
        int totalPages = (int)Math.Ceiling((double)totalCount / ServicesPerPage);
        if (totalPages == 0) totalPages = 1;
        if (page < 0) page = 0;
        if (page >= totalPages) page = totalPages - 1;

        var pageItems = await _db.GetServicesAsync(filterType, page * ServicesPerPage, ServicesPerPage);

        var filterName = filterType?.ToString() ?? "Все";
        var embed = new EmbedBuilder()
            .WithTitle($"🌐 Услуги: {filterName} (Стр. {page + 1} из {totalPages})")
            .WithColor(Color.Purple);

        if (pageItems.Count == 0)
        {
            embed.WithDescription("Услуг по данному фильтру не найдено.");
        }
        else
        {
            foreach (var service in pageItems)
            {
                string description = 
                    $"**Бустер:** <@{service.BoosterId}>\n" +
                    $"**MMR:** {service.MinMmr} - {service.MaxMmr}\n" +
                    $"**Цена:** {service.Price} ₽\n" +
                    $"[Steam профиль]({service.BoosterSteam})";

                embed.AddField($"🎮 {service.Name} ({service.Type})", description, inline: false);
            }
        }

        var builder = new ComponentBuilder();
        
        if (pageItems.Count > 0)
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId("select_service_to_buy")
                .WithPlaceholder("Выберите услугу для покупки")
                .WithMinValues(1).WithMaxValues(1);

            foreach (var s in pageItems)
            {
                menu.AddOption($"{s.Name} - {s.Price}₽", s.Id.ToString(), $"Тип: {s.Type}");
            }

            builder.WithSelectMenu(menu);
            builder.WithButton("💰 Купить выбранное", "buy_service_btn", ButtonStyle.Success);
        }

        builder.WithButton("Все", "filter_all", ButtonStyle.Secondary, row: 1)
               .WithButton("Буст", "filter_boost", ButtonStyle.Primary, row: 1)
               .WithButton("Калибровка", "filter_calibration", ButtonStyle.Primary, row: 1)
               .WithButton("ЛП", "filter_lp", ButtonStyle.Primary, row: 1)
               .WithButton("⬅ Назад", "menu_back", ButtonStyle.Danger, row: 1);

        string filterStr = filterType?.ToString() ?? "all";
        builder.WithButton("◀ Назад", $"page_{filterStr}_{page - 1}", ButtonStyle.Secondary, row: 2, disabled: page <= 0)
               .WithButton("Вперед ▶", $"page_{filterStr}_{page + 1}", ButtonStyle.Secondary, row: 2, disabled: page >= totalPages - 1);

        await component.UpdateAsync(m => { m.Embed = embed.Build(); m.Components = builder.Build(); });
    }

    // ==========================================
    // ПОКУПКА
    // ==========================================

    public async Task HandleBuyServiceAsync(SocketMessageComponent component)
    {
        // 1. СРАЗУ подтверждаем клик, чтобы Discord не выдал ошибку таймаута
        await component.DeferAsync(ephemeral: true);

        // 2. Достаем сохраненный выбор
        if (!_userSelections.TryGetValue(component.User.Id, out int serviceId))
        {
            await component.FollowupAsync("❌ Ошибка: Вы не выбрали услугу. Сначала выберите её в меню!", ephemeral: true);
            return;
        }

        // 3. Ищем услугу в БД
        var service = await _db.GetServiceByIdAsync(serviceId);
        if (service == null)
        {
            await component.FollowupAsync("❌ Услуга не найдена.", ephemeral: true);
            return;
        }

        // 4. Создаем заказ в БД
        var order = await _db.CreateOrderAsync(
            clientId: component.User.Id,
            boosterId: service.BoosterId,
            serviceId: service.Id,
            price: service.Price
        );

        // 5. Создаем ссылку через BEpusdt
        string webhookUrl = "https://lion-kotlas.ru/api/webhooks/bepusdt"; 
        string redirectUrl = "https://lion-kotlas.ru";
        
        // ВАЖНО: Для теста списываем 1.00 USD. 
        // BEpusdt сам сконвертирует это в USDT (TRC-20) по курсу.
        decimal testAmount = 1.00m; 
        
        var payUrl = await _bepusdt.CreateInvoiceAsync(
            orderId: order.Id.ToString(),
            amount: testAmount, 
            orderDesc: service.Name,
            webhookUrl: webhookUrl,
            redirectUrl: redirectUrl
        );

        if (payUrl == null)
        {
            await component.FollowupAsync("❌ Ошибка при создании счета на оплату. Попробуйте позже.", ephemeral: true);
            return;
        }

        // 6. Отправляем ссылку пользователю
        var embed = new EmbedBuilder()
            .WithTitle("💳 Счет на оплату создан")
            .WithDescription($"**Услуга:** {service.Name}\n**Сумма:** {service.Price} ₽\n\n" +
                             $"Оплатите по ссылке ниже. Счет действителен 15 минут.\n" +
                             $"[💸 Перейти к оплате]({payUrl})")
            .WithColor(Color.Green)
            .Build();

        // Используем FollowupAsync, так как уже сделали DeferAsync
        await component.FollowupAsync(embed: embed, ephemeral: true);
    }

    // ==========================================
    // МОДАЛКА ДОБАВЛЕНИЯ УСЛУГИ
    // ==========================================

    public async Task OpenAddServiceModalAsync(SocketMessageComponent component)
    {
        var modal = new ModalBuilder()
            .WithTitle("Размещение услуги")
            .WithCustomId("modal_create_service")
            .AddTextInput("Название услуги", "service_name", placeholder: "Например: Буст MMR", minLength: 3)
            .AddTextInput("Стоимость (в рублях)", "service_price", placeholder: "Только число: 1500")
            .AddTextInput("Тип услуги", "service_type", placeholder: "Boost, Calibration или LP")
            .AddTextInput("Диапазон MMR", "mmr_range", placeholder: "Мин-Макс через дефис. Напр: 1000-3000")
            .AddTextInput("Ссылка на Steam профиль", "steam_link", placeholder: "https://steamcommunity.com/id/...");

        await component.RespondWithModalAsync(modal.Build());
    }
}