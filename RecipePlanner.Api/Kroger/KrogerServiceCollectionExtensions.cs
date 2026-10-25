namespace RecipePlanner.Api.Kroger;

public static class KrogerServiceCollectionExtensions
{
    public static IServiceCollection AddKroger(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KrogerOptions>(configuration.GetSection("Kroger"));
        services.AddSingleton<KrogerAppTokenCache>();
        services.AddScoped<KrogerTokens>();
        var baseUrl = configuration["Kroger:BaseUrl"] ?? new KrogerOptions().BaseUrl;
        services.AddHttpClient<KrogerClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        return services;
    }
}
