using Microsoft.Extensions.Options;

namespace RecipePlanner.Api.Photos;

public static class PhotoServiceCollectionExtensions
{
    public static IServiceCollection AddRecipePhotos(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PhotoStorageOptions>(configuration.GetSection("PhotoStorage"));
        services.AddSingleton<BlobPhotoStore>();

        // Nothing configured: the app still works, it just says photos aren't set up
        services.AddSingleton<IPhotoStore>(sp =>
            sp.GetRequiredService<IOptions<PhotoStorageOptions>>().Value.IsConfigured
                ? sp.GetRequiredService<BlobPhotoStore>()
                : new DisabledPhotoStore());
        return services;
    }
}
