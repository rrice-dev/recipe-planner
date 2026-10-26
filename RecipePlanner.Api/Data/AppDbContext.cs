using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Data;

public static class RecipeLimits
{
    public const int MaxNotesLength = 4000;
    public const int MaxCollectionNameLength = 60;
    public const int MaxCollectionsPerUser = 50;
    public const int MaxSourceUrlLength = 2000;
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options), IDataProtectionKeyContext
{
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Technique> Techniques => Set<Technique>();
    public DbSet<MealPlan> MealPlans => Set<MealPlan>();
    public DbSet<CookLogEntry> CookLog => Set<CookLogEntry>();
    public DbSet<RecipeCollection> RecipeCollections => Set<RecipeCollection>();
    public DbSet<RecipeRating> RecipeRatings => Set<RecipeRating>();
    public DbSet<GroceryItem> GroceryItems => Set<GroceryItem>();
    public DbSet<KrogerConnection> KrogerConnections => Set<KrogerConnection>();
    public DbSet<KrogerProductChoice> KrogerProductChoices => Set<KrogerProductChoice>();

    // The keys that encrypt login cookies. Stored here so every container uses the same keys,
    // and logins survive restarts and scale-to-zero.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b); // Required: sets up Identity's tables

        b.Entity<RecipeIngredient>().HasKey(ri => new { ri.RecipeId, ri.IngredientId });
        b.Entity<RecipeIngredient>().Property(ri => ri.Quantity).HasPrecision(10, 2);
        b.Entity<Ingredient>().HasIndex(i => i.Name).IsUnique();
        b.Entity<Technique>().HasIndex(t => t.Name).IsUnique();

        // Every recipe and meal plan belongs to a user
        b.Entity<Recipe>()
            .HasOne<AppUser>().WithMany()
            .HasForeignKey(r => r.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<Recipe>().HasIndex(r => r.ShareToken).IsUnique();
        b.Entity<Recipe>().Property(r => r.ShareToken).HasMaxLength(32);

        b.Entity<Recipe>().Property(r => r.PhotoName).HasMaxLength(64);
        b.Entity<Recipe>().Property(r => r.LibrarySlug).HasMaxLength(80);
        b.Entity<Recipe>().Property(r => r.SourceUrl).HasMaxLength(RecipeLimits.MaxSourceUrlLength);
        b.Entity<Recipe>().HasIndex(r => new { r.OwnerId, r.LibrarySlug });

        b.Entity<AppUser>().Property(u => u.Theme).HasMaxLength(10).HasDefaultValue(UserSettings.SystemTheme);
        b.Entity<AppUser>().Property(u => u.Diet).HasMaxLength(20);
        b.Entity<AppUser>().Property(u => u.DefaultServings).HasDefaultValue(UserSettings.DefaultServingsValue);
        b.Entity<AppUser>().Property(u => u.Units).HasMaxLength(10).HasDefaultValue(UserSettings.UsUnits);

        b.Entity<Recipe>().Property(r => r.Notes).HasMaxLength(RecipeLimits.MaxNotesLength);
        b.Entity<CookLogEntry>()
            .HasOne<Recipe>().WithMany(r => r.CookLog)
            .HasForeignKey(c => c.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<CookLogEntry>().HasIndex(c => new { c.RecipeId, c.CookedOn });

        b.Entity<RecipeCollection>().Property(c => c.Name).HasMaxLength(RecipeLimits.MaxCollectionNameLength);
        b.Entity<RecipeCollection>().HasIndex(c => new { c.OwnerId, c.Name }).IsUnique();
        b.Entity<RecipeCollection>()
            .HasOne<AppUser>().WithMany()
            .HasForeignKey(c => c.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
        // Deleting a collection or a recipe only removes the link between them, never the other side
        b.Entity<RecipeCollection>().HasMany(c => c.Recipes).WithMany(r => r.Collections)
            .UsingEntity(j => j.ToTable("RecipeCollectionItems"));

        b.Entity<RecipeRating>().HasKey(r => new { r.RecipeId, r.UserId });
        b.Entity<RecipeRating>().ToTable(t => t.HasCheckConstraint("CK_RecipeRatings_Stars", "\"Stars\" BETWEEN 1 AND 5"));
        b.Entity<RecipeRating>()
            .HasOne<Recipe>().WithMany(r => r.Ratings)
            .HasForeignKey(r => r.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<RecipeRating>()
            .HasOne<AppUser>().WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<GroceryItem>().Property(g => g.Name).HasMaxLength(100);
        b.Entity<GroceryItem>().Property(g => g.Unit).HasMaxLength(20);
        b.Entity<GroceryItem>().Property(g => g.Sources).HasMaxLength(200);
        b.Entity<GroceryItem>().Property(g => g.Quantity).HasPrecision(10, 2);
        b.Entity<GroceryItem>().HasIndex(g => g.OwnerId);
        b.Entity<GroceryItem>()
            .HasOne<AppUser>().WithMany()
            .HasForeignKey(g => g.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<GroceryItem>()
            .HasOne(g => g.Ingredient).WithMany()
            .HasForeignKey(g => g.IngredientId)
            .OnDelete(DeleteBehavior.SetNull);   // deleting a shared ingredient keeps the line as free text

        b.Entity<KrogerConnection>().HasKey(k => k.UserId);
        b.Entity<KrogerConnection>().Property(k => k.LocationId).HasMaxLength(20);
        b.Entity<KrogerConnection>().Property(k => k.LocationName).HasMaxLength(200);
        b.Entity<KrogerConnection>()
            .HasOne<AppUser>().WithMany()
            .HasForeignKey(k => k.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<KrogerProductChoice>().HasKey(k => new { k.UserId, k.ItemKey });
        b.Entity<KrogerProductChoice>().Property(k => k.ItemKey).HasMaxLength(100);
        b.Entity<KrogerProductChoice>().Property(k => k.Upc).HasMaxLength(20);
        b.Entity<KrogerProductChoice>().Property(k => k.Description).HasMaxLength(200);
        b.Entity<KrogerProductChoice>()
            .HasOne<AppUser>().WithMany()
            .HasForeignKey(k => k.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<MealPlan>()
            .HasOne<AppUser>().WithMany()
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}