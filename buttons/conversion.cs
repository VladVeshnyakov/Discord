using Discord;

namespace DiscordBirge.Views;

public class PaymentMethod
{
    public string name { get; set; }
    public decimal translationProcent { get; set; }
}

public class ConversionTable
{
    public Embed ConversionTableClient(PaymentMethod[] payments, decimal price_of_the_service)
    {
        var embed = new EmbedBuilder()
            .WithTitle("💳 Способы оплаты")
            .WithDescription($"Стоимость услуги: **{price_of_the_service} ₽**")
            .WithColor(Color.Gold);
        
        foreach (var pay in payments)
        {
            decimal commission = price_of_the_service * (pay.translationProcent / 100);
            decimal finalPrice = price_of_the_service + commission;
            
            embed.AddField(
                $"**{pay.name}**", 
                $"Комиссия: {pay.translationProcent}%\nК оплате: **{finalPrice:F2} ₽**", 
                inline: false
            );
        }
        
        return embed.Build();
    } 
    public Embed ConversionTableBooster(PaymentMethod[] payments, decimal price_of_the_service)
    {
        var embed = new EmbedBuilder()
            .WithTitle("💳 Способы вывода")
            .WithDescription($"Стоимость услуги: **{price_of_the_service} ₽**")
            .WithColor(Color.Gold);
        
        foreach (var pay in payments)
        {
            decimal commission = price_of_the_service * (pay.translationProcent / 100);
            decimal finalPrice = price_of_the_service - commission; 

            embed.AddField(
                $"**{pay.name}**", 
                $"Комиссия: {pay.translationProcent}%\nВывод: **{finalPrice:F2} ₽**", 
                inline: false
            );
        }
        
        return embed.Build();
    }
}