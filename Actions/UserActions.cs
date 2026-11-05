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

    private static readonly Dictionary<ulong, int> _userSelections = new();

    private readonly OrderChannelService _orderChannels;

    public UserActions(DbService db, BepusdtService bepusdt, OrderChannelService orderChannels)
    {
        _db = db;
        _bepusdt = bepusdt;
        _orderChannels = orderChannels;
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
                string priceLabel = service.Type == ServiceType.LP ? $"{service.Price} ₽ / игра" : $"{service.Price} ₽";
                string description = 
                    $"**Бустер:** <@{service.BoosterId}>\n" +
                    (service.Type == ServiceType.Boost ? $"**MMR:** {service.MinMmr} - {service.MaxMmr}\n" : "") +
                    $"**Цена:** {priceLabel}\n" +
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
        if (!_userSelections.TryGetValue(component.User.Id, out int serviceId))
        {
            await component.RespondAsync("❌ Ошибка: Вы не выбрали услугу. Сначала выберите её в меню!", ephemeral: true);
            return;
        }

        var service = await _db.GetServiceByIdAsync(serviceId);
        if (service == null)
        {
            await component.RespondAsync("❌ Услуга не найдена.", ephemeral: true);
            return;
        }

        // Если это LP - открываем модалку для ввода количества игр
        if (service.Type == ServiceType.LP)
        {
            var modal = new ModalBuilder()
                .WithTitle("Сколько игр LP отыграть?")
                .WithCustomId($"modal_lp_{serviceId}")
                .AddTextInput("Количество игр", "games_count", placeholder: "Например: 5", minLength: 1, maxLength: 2);
            
            await component.RespondWithModalAsync(modal.Build());
            return;
        }

        // Для остальных услуг - стандартная оплата
        await component.DeferAsync(ephemeral: true);

        var order = await _db.CreateOrderAsync(component.User.Id, service.BoosterId, service.Id, service.Price);

        string webhookUrl = "https://lion-kotlas.ru/api/webhooks/bepusdt"; 
        string redirectUrl = "https://lion-kotlas.ru";
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
            await component.FollowupAsync("❌ Ошибка при создании счета на оплату.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("💳 Счет на оплату создан")
            .WithDescription($"**Услуга:** {service.Name}\n**Сумма:** {service.Price} ₽\n\n[💸 Перейти к оплате]({payUrl})")
            .WithColor(Color.Green)
            .Build();

        await component.FollowupAsync(embed: embed, ephemeral: true);
    }

    // ==========================================
    // КАБИНЕТ БУСТЕРА
    // ==========================================

    public async Task ShowMyServicesAsync(SocketMessageComponent component)
    {
        var orders = await _db.GetBoosterActiveOrdersAsync(component.User.Id);
        
        if (orders.Count == 0)
        {
            await component.RespondAsync("📦 У вас нет активных заказов в работе.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("🛠 Ваши активные заказы")
            .WithDescription("Управляйте сменой для каждого заказа.")
            .WithColor(Color.Green);

        var builder = new ComponentBuilder();
        int row = 0;

        foreach (var order in orders.Take(4)) 
        {
            string shiftStatus = order.IsShiftActive ? "🟢 Смена активна" : "🔴 Смена не начата";
            
            string progressText = "";
            if (order.Service.Type == ServiceType.Boost || order.Service.Type == ServiceType.Calibration)
            {
                progressText = $"**MMR:** {order.CurrentMmr} / {order.TargetMmr} (дельта +{order.TargetMmrDelta})";
            }
            else if (order.Service.Type == ServiceType.LP)
            {
                progressText = $"**Игры LP:** {order.TotalGamesPlayed} / {order.TargetGames}";
            }
            else if (order.Service.Type == ServiceType.Behavior)
            {
                progressText = $"**Порядочность:** Завершается вручную (Сыграно игр: {order.TotalGamesPlayed})";
            }

            string label = $"#{order.Id}: {(order.IsShiftActive ? "Закончить" : "Начать")}";
            ButtonStyle style = order.IsShiftActive ? ButtonStyle.Danger : ButtonStyle.Success;
            string btnId = order.IsShiftActive ? $"end_shift_{order.Id}" : $"start_shift_{order.Id}";
            builder.WithButton(label, btnId, style, row: row);

            embed.AddField(
                $"Заказ #{order.Id} | {order.Service.Type}",
                $"**Клиент:** <@{order.ClientId}>\n" +
                $"{progressText}\n" +
                $"**Игры:** бустер {order.BoosterGamesPlayed}, владелец {order.OwnerGamesPlayed}\n" +
                $"**Цена:** {order.Price} ₽ | **Статус:** {shiftStatus}",
                inline: false
            );
            row++;
        }

        builder.WithButton("⬅ Назад", "menu_services", ButtonStyle.Secondary, row: row);
        await component.UpdateAsync(m => { m.Embed = embed.Build(); m.Components = builder.Build(); });
    }
    
    public async Task StartShiftAsync(SocketMessageComponent component, int orderId)
    {
        await component.DeferAsync(ephemeral: true);
        var order = await _db.GetOrderByIdAsync(orderId);
    
        if (order == null || order.BoosterId != component.User.Id)
        {
            await component.FollowupAsync("❌ Заказ не найден или он не ваш.", ephemeral: true);
            return;
        }

        if (order.IsShiftActive)
        {
            await component.FollowupAsync("⚠️ Смена уже активна.", ephemeral: true);
            return;
        }

        order.IsShiftActive = true;
        order.ShiftStartedAt = DateTime.UtcNow;
    
        if (order.Service.Type == ServiceType.Boost || order.Service.Type == ServiceType.Calibration)
            order.TargetMmr = order.CurrentMmr + order.TargetMmrDelta;
    
        await _db.UpdateOrderAsync(order);
    
        // Обновляем панель в каналах
        await _orderChannels.UpdateChannelsProgressAsync(order);
    
        await component.FollowupAsync("✅ Смена начата. Удачи!", ephemeral: true);
    }

    public async Task EndShiftAsync(SocketMessageComponent component, int orderId)
    {
        await component.DeferAsync(ephemeral: true);
        var order = await _db.GetOrderByIdAsync(orderId);
    
        if (order == null || order.BoosterId != component.User.Id)
        {
            await component.FollowupAsync("❌ Заказ не найден.", ephemeral: true);
            return;
        }

        order.IsShiftActive = false;
        await _db.UpdateOrderAsync(order);
    
        await _orderChannels.UpdateChannelsProgressAsync(order);
    
        await component.FollowupAsync("🛑 Смена завершена.", ephemeral: true);
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
            .AddTextInput("Тип услуги", "service_type", placeholder: "Boost, Calibration, LP или Behavior")
            .AddTextInput("Диапазон MMR", "mmr_range", placeholder: "Мин-Макс (для буста, иначе 0-0)", required: false)
            .AddTextInput("Ссылка на Steam профиль", "steam_link", placeholder: "https://steamcommunity.com/id/...");

        await component.RespondWithModalAsync(modal.Build());
    }
    public async Task CreatePrivateChannelAsync(IGuild guild, IGuildUser user, string channelName)
    {
        var everyoneRole = guild.EveryoneRole;
        var channelPermissions = new List<Overwrite>
        {
            new Overwrite(
                everyoneRole.Id, 
                PermissionTarget.Role, 
                new OverwritePermissions(
                    viewChannel: PermValue.Deny,
                    sendMessages: PermValue.Deny
                )
            ),
            new Overwrite(
                user.Id, 
                PermissionTarget.User, 
                new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    readMessageHistory: PermValue.Allow
                )
            )
        };

        // 2. Создаем канал
        var channel = await guild.CreateTextChannelAsync(channelName, x =>
        {
            x.PermissionOverwrites = channelPermissions;
            x.Topic = $"Личный канал пользователя {user.Username}";
        });

        // 3. Отправляем приветствие
        var embed = new EmbedBuilder()
            .WithTitle("💼 Ваш личный канал")
            .WithDescription($"Привет, {user.Mention}! Здесь вы можете общаться с бустером.")
            .WithColor(Color.Blue)
            .Build();

        await channel.SendMessageAsync(embed: embed);
        Console.WriteLine($"[CHANNEL] Создан приватный канал #{channel.Name} для {user.Username}");
    }
}