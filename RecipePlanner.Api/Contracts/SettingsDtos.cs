namespace RecipePlanner.Api.Contracts;

public record SettingsResponse(string Theme, string? Diet, int DefaultServings, string Units = "us");

/// <param name="Units">"us" or "metric"; null keeps the current choice</param>
public record SettingsRequest(string Theme, string? Diet, int DefaultServings, string? Units = null);

public record DeleteAccountRequest(string Password);
