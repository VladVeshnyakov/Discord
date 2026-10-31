namespace DiscordBirge.modals;
using Discord;
public class CreateService
{
    public async Task ShowCreateServiceModalAsync(IDiscordInteraction interaction)
    {
        var mb = new ModalBuilder()
            .WithTitle("Создание новой услуги")
            .WithCustomId("create_service_modal")
            .AddTextInput("Название услуги", "service_name", 
                placeholder: "Например: Буст MMR до 5000", 
                minLength: 3, maxLength: 100)
            .AddTextInput("Стоимость (в рублях)", "service_price", 
                placeholder: "просто целочисленное число", 
                minLength: 1, maxLength: 10);
    
        await interaction.RespondWithModalAsync(mb.Build());
    }
}