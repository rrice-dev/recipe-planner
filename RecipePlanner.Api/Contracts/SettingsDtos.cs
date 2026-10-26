namespace RecipePlanner.Api.Contracts;

public record SettingsResponse(string Theme, string? Diet, int DefaultServings);

public record SettingsRequest(string Theme, string? Diet, int DefaultServings);

public record DeleteAccountRequest(string Password);
