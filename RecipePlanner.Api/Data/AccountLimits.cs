namespace RecipePlanner.Api.Data;

// How much one account can keep. Far more than a real recipe book needs, and they stop one account
// from filling the database (the per-minute rate limit alone would still allow thousands a day).
public static class AccountLimits
{
    public const int MaxRecipes = 2_000;
    public const int MaxMealPlans = 200;
    public const int MaxCookLogEntries = 1_000;   // per recipe: cooked daily for almost three years

    public static IResult TooManyRecipes() => Refuse("Recipes",
        $"You can keep up to {MaxRecipes:N0} recipes. Delete some you don't use to add more.");

    public static IResult TooManyMealPlans() => Refuse("MealPlans",
        $"You can keep up to {MaxMealPlans} meal plans. Delete some old ones to add more.");

    public static IResult TooManyCookLogEntries() => Refuse("CookedOn",
        $"A recipe can keep up to {MaxCookLogEntries:N0} cooked dates. Remove some old ones to add more.");

    private static IResult Refuse(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
