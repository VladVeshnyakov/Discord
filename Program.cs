using Discord;
using Discord.WebSocket;
using DiscordBirge;
using DiscordBirge.Data;
using DiscordBirge.Helpers;
using DiscordBirge.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds |
                     GatewayIntents.GuildMessages |
                     GatewayIntents.MessageContent |
                     GatewayIntents.GuildMembers
}));

builder.Services.AddScoped<DbService>();
builder.Services.AddHttpClient<BepusdtService>();
builder.Services.AddScoped<OrderChannelService>();
builder.Services.AddHttpClient<OpenDotaService>();
builder.Services.AddSingleton<InfoMessageService>();
builder.Services.AddHostedService<MatchTrackerService>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton<BotService>();

var app = builder.Build();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
    await DbSeeder.SeedAsync(dbContext);
}

var bot = app.Services.GetRequiredService<BotService>();
await bot.StartAsync();

await app.RunAsync();