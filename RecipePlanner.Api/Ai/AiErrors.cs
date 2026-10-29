namespace RecipePlanner.Api.Ai;

/// <summary>The app-wide daily AI limit is used up (see AiDailyLimit). Lasts until midnight UTC, not a minute.</summary>
public class AiDailyLimitReachedException()
    : MealIdeaException("AI provider returned 429: the app's daily AI limit is used up.");

public static class AiErrors
{
    public const string DailyLimitMessage =
        "AI features are paused for the rest of the day because the site reached its daily limit. Try again tomorrow.";

    /// <summary>What to tell the person when an AI call failed: the daily limit says so; anything else gets the page's own message</summary>
    public static string Message(Exception ex, string otherwise) =>
        ex is AiDailyLimitReachedException ? DailyLimitMessage : otherwise;
}
