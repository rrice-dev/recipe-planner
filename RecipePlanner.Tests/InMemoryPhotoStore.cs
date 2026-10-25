using System.Collections.Concurrent;
using RecipePlanner.Api.Photos;

namespace RecipePlanner.Tests;

public class InMemoryPhotoStore : IPhotoStore
{
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();
    public bool IsAvailable => true;

    public Task SaveAsync(string name, byte[] jpeg, CancellationToken ct) { Files[name] = jpeg; return Task.CompletedTask; }
    public Task<byte[]?> ReadAsync(string name, CancellationToken ct) => Task.FromResult(Files.GetValueOrDefault(name));
    public Task DeleteAsync(string name, CancellationToken ct) { Files.TryRemove(name, out _); return Task.CompletedTask; }
}
