using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Data;

namespace RecipePlanner.Tests;

public class SeedDataTests
{
    private static string ApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "RecipePlanner.Api")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "RecipePlanner.Api");
    }

    [Fact]
    public async Task Seed_techniques_are_valid_and_seeding_twice_adds_nothing()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);

        await DbSeeder.SeedAsync(db, ApiContentRoot());
        var techniques = await db.Techniques.ToListAsync();

        Assert.True(techniques.Count >= 39);
        Assert.All(techniques, t =>
        {
            Assert.InRange(t.Difficulty, 1, 5);
            Assert.False(string.IsNullOrWhiteSpace(t.Summary));
            Assert.True(t.Name.Length <= 100);
        });
        Assert.Equal(techniques.Count, techniques.Select(t => t.Name.ToLowerInvariant()).Distinct().Count());

        // Runs on every API start, so it must never duplicate anything
        await DbSeeder.SeedAsync(db, ApiContentRoot());
        Assert.Equal(techniques.Count, await db.Techniques.CountAsync());
    }
}
