using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Technique> Techniques => Set<Technique>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<RecipeIngredient>().HasKey(ri => new { ri.RecipeId, ri.IngredientId });
        b.Entity<RecipeIngredient>().Property(ri => ri.Quantity).HasPrecision(10, 2);
        b.Entity<Ingredient>().HasIndex(i => i.Name).IsUnique();
        b.Entity<Technique>().HasIndex(t => t.Name).IsUnique();
    }
}