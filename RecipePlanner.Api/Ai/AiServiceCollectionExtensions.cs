using Microsoft.Extensions.Options;

namespace RecipePlanner.Api.Ai;

public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddMealIdeas(this IServiceCollection services, IConfiguration configuration)
    {
        // Reads Anthropic:ApiKey (env var Anthropic__ApiKey). Never put the key in appsettings.json.
        services.Configure<AnthropicOptions>(configuration.GetSection("Anthropic"));

        services.AddHttpClient<AnthropicMealIdeaGenerator>(client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<AnthropicIngredientScanner>(client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com/");
            client.Timeout = TimeSpan.FromSeconds(45); // Images take a little longer
        });

        // No key configured: the feature still works, just without AI ideas
        services.AddScoped<IMealIdeaGenerator>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<AnthropicOptions>>().Value.ApiKey)
                ? new DisabledMealIdeaGenerator()
                : sp.GetRequiredService<AnthropicMealIdeaGenerator>());

        services.AddHttpClient<AnthropicRecipeDraftGenerator>(client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com/");
            client.Timeout = TimeSpan.FromSeconds(60); // Longer answers take longer to write
        });

        services.AddScoped<IRecipeDraftGenerator>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<AnthropicOptions>>().Value.ApiKey)
                ? new DisabledRecipeDraftGenerator()
                : sp.GetRequiredService<AnthropicRecipeDraftGenerator>());

        services.AddScoped<IIngredientScanner>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<AnthropicOptions>>().Value.ApiKey)
                ? new DisabledIngredientScanner()
                : sp.GetRequiredService<AnthropicIngredientScanner>());

        services.AddHttpClient<AnthropicCountryDishGenerator>(client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<ICountryDishGenerator>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<AnthropicOptions>>().Value.ApiKey)
                ? new DisabledCountryDishGenerator()
                : sp.GetRequiredService<AnthropicCountryDishGenerator>());

        services.AddHttpClient<AnthropicRecipeExtractor>(client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com/");
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddScoped<IRecipeExtractor>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<AnthropicOptions>>().Value.ApiKey)
                ? new DisabledRecipeExtractor()
                : sp.GetRequiredService<AnthropicRecipeExtractor>());

        return services;
    }
}
