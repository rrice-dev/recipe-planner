namespace RecipePlanner.Api.Photos;

// Where recipe photos live. The app only uses this interface, so storage can be swapped
// (Azure Blob in production, in-memory in tests) without touching the endpoints.
public interface IPhotoStore
{
    bool IsAvailable { get; }
    Task SaveAsync(string name, byte[] jpeg, CancellationToken ct);
    Task<byte[]?> ReadAsync(string name, CancellationToken ct);
    Task DeleteAsync(string name, CancellationToken ct);
}

public sealed class DisabledPhotoStore : IPhotoStore
{
    public bool IsAvailable => false;
    public Task SaveAsync(string name, byte[] jpeg, CancellationToken ct) => throw new InvalidOperationException("Photo storage isn't configured.");
    public Task<byte[]?> ReadAsync(string name, CancellationToken ct) => Task.FromResult<byte[]?>(null);
    public Task DeleteAsync(string name, CancellationToken ct) => Task.CompletedTask;
}
