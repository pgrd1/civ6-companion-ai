using System.Text.Json;
using System.Text.Json.Serialization;

namespace Civ6Companion.App.Advisor;

public sealed class ScreenTypeJsonConverter : JsonConverter<ScreenType>
{
    private static readonly IReadOnlyDictionary<string, ScreenType> FromWire = new Dictionary<string, ScreenType>(StringComparer.Ordinal)
    {
        ["map"] = ScreenType.Map, ["city_production"] = ScreenType.CityProduction,
        ["technology"] = ScreenType.Technology, ["civic"] = ScreenType.Civic,
        ["government"] = ScreenType.Government, ["great_person"] = ScreenType.GreatPerson,
        ["city_state"] = ScreenType.CityState, ["diplomacy"] = ScreenType.Diplomacy,
        ["trade"] = ScreenType.Trade, ["citizen_management"] = ScreenType.CitizenManagement,
        ["religion"] = ScreenType.Religion, ["other"] = ScreenType.Other
    };

    public static IReadOnlyCollection<string> WireValues => FromWire.Keys.ToArray();

    public override ScreenType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return value is not null && FromWire.TryGetValue(value, out var result)
            ? result : throw new JsonException($"Unknown screenType '{value}'.");
    }

    public override void Write(Utf8JsonWriter writer, ScreenType value, JsonSerializerOptions options)
    {
        var wire = FromWire.FirstOrDefault(pair => pair.Value == value).Key;
        if (wire is null) throw new JsonException($"Unknown screenType '{value}'.");
        writer.WriteStringValue(wire);
    }
}
