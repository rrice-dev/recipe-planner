using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace RecipePlanner.Api.Photos;

public class PhotoStorageOptions
{
    // Production: the storage account's address. The API signs in with its managed identity,
    // so there is no storage key or password anywhere.
    public string? BlobServiceUri { get; set; }

    // Local development only (Azurite, the storage emulator in docker compose)
    public string? ConnectionString { get; set; }

    public string Container { get; set; } = "recipe-photos";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BlobServiceUri) || !string.IsNullOrWhiteSpace(ConnectionString);
}

public class BlobPhotoStore(IOptions<PhotoStorageOptions> options, ILogger<BlobPhotoStore> logger) : IPhotoStore
{
    private readonly BlobContainerClient _container = CreateClient(options.Value);
    private readonly SemaphoreSlim _createLock = new(1, 1);
    private bool _containerReady;

    public bool IsAvailable => true;

    private static BlobContainerClient CreateClient(PhotoStorageOptions o) =>
        !string.IsNullOrWhiteSpace(o.ConnectionString)
            ? new BlobContainerClient(o.ConnectionString, o.Container)
            : new BlobServiceClient(new Uri(o.BlobServiceUri!), new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned))
                .GetBlobContainerClient(o.Container);

    public async Task SaveAsync(string name, byte[] jpeg, CancellationToken ct)
    {
        await EnsureContainerAsync(ct);
        await _container.GetBlobClient(name).UploadAsync(new BinaryData(jpeg), new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "image/jpeg" }
        }, ct);
    }

    public async Task<byte[]?> ReadAsync(string name, CancellationToken ct)
    {
        try
        {
            var result = await _container.GetBlobClient(name).DownloadContentAsync(ct);
            return result.Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string name, CancellationToken ct)
    {
        try
        {
            await _container.GetBlobClient(name).DeleteIfExistsAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex)
        {
            // An orphaned file costs a fraction of a cent; failing the user's request over it isn't worth it
            logger.LogWarning(ex, "Couldn't delete old photo {Name}", name);
        }
    }

    // Create the container the first time it's needed. The container is private: photos are only ever
    // served through the API, which checks who is allowed to see each recipe.
    private async Task EnsureContainerAsync(CancellationToken ct)
    {
        if (_containerReady) return;
        await _createLock.WaitAsync(ct);
        try
        {
            if (!_containerReady)
            {
                await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
                _containerReady = true;
            }
        }
        finally
        {
            _createLock.Release();
        }
    }
}
