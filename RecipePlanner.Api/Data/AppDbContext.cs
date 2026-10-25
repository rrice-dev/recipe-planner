using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options), IDataProtectionKeyContext
{
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Technique> Techniques => Set<Technique>();
    public DbSet<MealPlan> MealPlans => Set<MealPlan>();
    public DbSet<RecipeRating> RecipeRatings => Set<RecipeRating>();

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

        b.Entity<MealPlan>()
            .HasOne<AppUser>().WithMany()
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}