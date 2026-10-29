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

    // Recipe text. Checked by the API rather than the database, so recipes saved before these limits still load.
    public const int MaxDescriptionLength = 2000;
    public const int MaxCuisineLength = 60;
    public const int MaxSteps = 100;
    public const int MaxStepLength = 2000;
    public const int MaxIngredients = 100;
    public const int MaxUnitLength = 20;      // shopping list units are stored in 20 characters
    public const int MaxIngredientNoteLength = 200;

    // The same servings range as meal plans, shopping lists and costs, so every recipe works with all of them
    public const int MaxServings = UserSettings.MaxServings;
    // A week, in minutes: room for a long cure or ferment, and prep plus cook can never overflow
    public const int MaxMinutes = 10_080;

    // Amounts are stored with 2 decimals (numeric(10,2)): below 0.01 rounds to 0, and the shopping list caps at 10,000
    public const decimal MinQuantity = 0.01m;
    public const decimal MaxQuantity = 10_000m;
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options), IDataProtectionKeyContext
{
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeSourcePage> RecipeSourcePages => Set<RecipeSourcePage>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Technique> Techniques => Set<Technique>();
    public DbSet<MealPlan> MealPlans => Set<MealPlan>();
    public DbSet<CookLogEntry> CookLog => Set<CookLogEntry>();
    public DbSet<RecipePlanner.Api.Nutrition.IngredientNutrition> IngredientNutrition => Set<RecipePlanner.Api.Nutrition.IngredientNutrition>();
    public DbSet<RecipeCollection> RecipeCollections => Set<RecipeCollection>();
    public DbSet<RecipeRating> RecipeRatings => Set<RecipeRating>();
    public DbSet<GroceryItem> GroceryItems => Set<GroceryItem>();
    public DbSet<KrogerConnection> KrogerConnections => Set<KrogerConnection>();
    public DbSet<KrogerProductChoice> KrogerProductChoices => Set<KrogerProductChoice>();
    public DbSet<RecipePlanner.Api.Kroger.IngredientPrice> IngredientPrices => Set<RecipePlanner.Api.Kroger.IngredientPrice>();
    public DbSet<RecipePlanner.Api.Kitchen.PantryItem> PantryItems => Set<RecipePlanner.Api.Kitchen.PantryItem>();
    public DbSet<RecipePlanner.Api.Kitchen.Leftover> Leftovers => Set<RecipePlanner.Api.Kitchen.Leftover>();

    // The keys that encrypt login cookies. Stored here so every container uses the same keys,
    // and logins survive restarts and scale-to-zero.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // One row per AI call, for the app-wide daily limit (see Ai/AiDailyLimit.cs)
    public DbSet<RecipePlanner.Api.Ai.AiUsage> AiUsage => Set<RecipePlanner.Api.Ai.AiUsage>();

    // When each account was last sent a confirmation or reset email (see Email/EmailCooldowns.cs)
    public DbSet<RecipePlanner.Api.Email.EmailCooldown> EmailCooldowns => Set<RecipePlanner.Api.Email.EmailCooldown>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b); // Required: sets up Identity's tables

        b.Entity<RecipeIngredient>().HasKey(ri => new { ri.RecipeId, ri.IngredientId });
        b.Entity<RecipeIngredient>().Property(ri => ri.Quantity).HasPrecision(10, 2);
        // Names are unique per owner, and among shared ingredients (NULLS NOT DISTINCT: without it Postgres
        // would let two shared rows have the same name, since NULL never equals NULL)
        b.Entity<Ingredient>().HasIndex(i => new { i.OwnerId, i.Name }).IsUnique().AreNullsDistinct(false);
        // A user's own ingredients go with their account
        b.Entity<Ingredient>().HasOne<AppUser>().WithMany().HasForeignKey(i => i.OwnerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Technique>().HasIndex(t => t.Name).IsUnique();

        // Every recipe and meal plan belongs to a user
        b.Entity<Recipe>()
            .HasOne<AppUser>().WithMany()
            .HasForeignKey(r => r.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<Recipe>().HasIndex(r => r.ShareToken).IsUnique();
        b.Entity<Recipe>().Property(r => r.ShareToken).HasMaxLength(32);

        b.Entity<Recipe>().Property(r => r.PhotoName).HasMaxLength(64);
        b.Entity<RecipeSourcePage>(e =>
        {
            e.Property(p => p.PhotoName).HasMaxLength(64);
            e.HasOne<Recipe>().WithMany(r => r.SourcePages).HasForeignKey(p => p.RecipeId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.RecipeId, p.Position });
        });
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

        b.Entity<RecipePlanner.Api.Nutrition.IngredientNutrition>().Property(n => n.NameKey).HasMaxLength(100);
        b.Entity<RecipePlanner.Api.Nutrition.IngredientNutrition>().HasIndex(n => n.NameKey).IsUnique();

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

        b.Entity<RecipePlanner.Api.Kroger.IngredientPrice>(e =>
        {
            e.Property(p => p.LocationId).HasMaxLength(20);
            e.Property(p => p.NameKey).HasMaxLength(100);
            e.HasIndex(p => new { p.LocationId, p.NameKey }).IsUnique();
        });
        b.Entity<AppUser>().Property(u => u.WeeklyBudget).HasPrecision(10, 2);

        b.Entity<RecipePlanner.Api.Kitchen.PantryItem>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(RecipePlanner.Api.Kitchen.KitchenRules.MaxNameLength);
            e.Property(p => p.NameKey).HasMaxLength(RecipePlanner.Api.Kitchen.KitchenRules.MaxNameLength);
            e.HasIndex(p => new { p.OwnerId, p.NameKey }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RecipePlanner.Api.Kitchen.Leftover>(e =>
        {
            e.Property(l => l.Name).HasMaxLength(RecipePlanner.Api.Kitchen.KitchenRules.MaxNameLength);
            e.Property(l => l.Location).HasMaxLength(10);
            e.HasIndex(l => l.OwnerId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(l => l.OwnerId).OnDelete(DeleteBehavior.Cascade);
            // Deleting the recipe keeps the leftovers; they're still in your fridge
            e.HasOne<Recipe>().WithMany().HasForeignKey(l => l.RecipeId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<RecipePlanner.Api.Ai.AiUsage>().HasIndex(u => u.UsedAt);

        b.Entity<RecipePlanner.Api.Email.EmailCooldown>(e =>
        {
            e.HasKey(c => new { c.UserId, c.Kind });
            e.Property(c => c.Kind).HasMaxLength(20);
            e.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MealPlan>()
            .HasOne<AppUser>().WithMany()
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}