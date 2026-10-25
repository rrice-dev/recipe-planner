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

        services.AddScoped<IIngredientScanner>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<AnthropicOptions>>().Value.ApiKey)
                ? new DisabledIngredientScanner()
                : sp.GetRequiredService<AnthropicIngredientScanner>());

        return services;
    }
}
