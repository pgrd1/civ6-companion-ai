using System.Text.Json;
using System.Text.Json.Serialization;

namespace Civ6Companion.App.Advisor;

[JsonConverter(typeof(ScreenTypeJsonConverter))]
public enum ScreenType { Map, CityProduction, Technology, Civic, Government, GreatPerson, CityState, Diplomacy, Trade, CitizenManagement, Religion, Other }

public sealed record RecommendedAction(string Action, string Reason);

public sealed record AnalysisResponse(
    ScreenType ScreenType,
    double Confidence,
    string Title,
    IReadOnlyList<RecommendedAction> ImmediateActions,
    IReadOnlyList<string> NextSteps,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> FiveTurnGoals,
    string? NeedsAnotherScreen,
    string StateUpdate);

public sealed record ChatResponse(string Message, string? NeedsAnotherScreen, string StateUpdate);

public sealed record ParseResult<T>(T? Value, IReadOnlyList<string> Errors, string RawText) where T : class
{
    public bool IsValid => Value is not null && Errors.Count == 0;
}

public static class AdvisorJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true
        };
        options.Converters.Add(new ScreenTypeJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
