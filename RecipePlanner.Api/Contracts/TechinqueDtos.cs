namespace RecipePlanner.Api.Contracts;

public record TechniqueRequest(
    string Name, string Summary, string? DonenessCues,
    string? CommonMistakes, int Difficulty);

public record TechniqueResponse(
    int Id, string Name, string Summary, string? DonenessCues,
    string? CommonMistakes, int Difficulty);

public record TechniqueSummary(int Id, string Name, int Difficulty);