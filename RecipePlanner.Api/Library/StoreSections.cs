namespace RecipePlanner.Api.Library;

// The order you'd walk through a typical grocery store. Matches store-sections.ts in the web app.
public static class StoreSections
{
    public static readonly string[] Order =
    [
        "Produce", "Meat", "Seafood", "Dairy", "Bakery", "Grains & Pasta", "Canned Goods", "Baking",
        "Oils & Vinegars", "Condiments & Sauces", "Spices", "Nuts & Seeds", "Frozen", "Beverages"
    ];

    /// <summary>Position in the walk; unknown or missing sections go last</summary>
    public static int Rank(string? section)
    {
        var i = section is null ? -1 : Array.IndexOf(Order, section);
        return i < 0 ? Order.Length : i;
    }
}
