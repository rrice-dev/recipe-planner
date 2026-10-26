using Microsoft.Extensions.Options;

namespace RecipePlanner.Api.Nutrition;

public static class NutritionServiceCollectionExtensions
{
    public static IServiceCollection AddNutrition(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NutritionOptions>(configuration.GetSection("Nutrition"));
        services.AddHttpClient<FoodDataCentralClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.nal.usda.gov/fdc/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        // No key configured: nutrition is simply hidden
        services.AddScoped<IFoodSource>(sp => sp.GetRequiredService<IOptions<NutritionOptions>>().Value.IsConfigured
            ? sp.GetRequiredService<FoodDataCentralClient>()
            : new DisabledFoodSource());
        services.AddScoped<NutritionService>();
        return services;
    }
}
