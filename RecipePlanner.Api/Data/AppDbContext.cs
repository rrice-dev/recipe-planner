using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Technique> Techniques => Set<Technique>();
    public DbSet<MealPlan> MealPlans => Set<MealPlan>();

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

        b.Entity<MealPlan>()
            .HasOne<AppUser>().WithMany()
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}