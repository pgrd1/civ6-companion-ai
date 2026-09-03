using System.Text.Json;
using System.Text.RegularExpressions;

namespace Civ6Companion.App.Advisor;

public static partial class AnalysisResponseValidator
{
    private const int MaxRaw = 8 * 1024;
    private const int MaxString = 600;
    private static readonly string[] AnalysisProperties = ["screenType", "confidence", "title", "immediateActions", "nextSteps", "warnings", "fiveTurnGoals", "needsAnotherScreen", "stateUpdate"];
    private static readonly string[] ChatProperties = ["message", "needsAnotherScreen", "stateUpdate"];

    public static ParseResult<AnalysisResponse> Parse(string raw) => ParseCore(raw, AnalysisProperties, ValidateAnalysis);
    public static ParseResult<ChatResponse> ParseChat(string raw) => ParseCore(raw, ChatProperties, ValidateChat);

    private static ParseResult<T> ParseCore<T>(string raw, IReadOnlyCollection<string> allowed, Func<JsonElement, List<string>, T?> validator) where T : class
    {
        var safeRaw = Sanitize(raw);
        var json = ExtractJson(raw);
        var errors = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new(null, ["응답은 JSON 객체여야 합니다."], safeRaw);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!allowed.Contains(property.Name, StringComparer.Ordinal)) errors.Add($"허용되지 않은 속성: {property.Name}");
            foreach (var required in allowed)
                if (!document.RootElement.TryGetProperty(required, out _)) errors.Add($"필수 속성 누락: {required}");
            var value = validator(document.RootElement, errors);
            return errors.Count == 0 ? new(value, Array.Empty<string>(), safeRaw) : new(default, errors, safeRaw);
        }
        catch (JsonException ex)
        {
            errors.Add($"JSON 형식 오류: {ex.Message}");
            return new(default, errors, safeRaw);
        }
    }

    private static AnalysisResponse? ValidateAnalysis(JsonElement root, List<string> errors)
    {
        ValidateString(root, "title", errors); ValidateString(root, "stateUpdate", errors); ValidateNullableString(root, "needsAnotherScreen", errors);
        ValidateStringArray(root, "nextSteps", errors); ValidateStringArray(root, "warnings", errors); ValidateStringArray(root, "fiveTurnGoals", errors);
        if (!root.TryGetProperty("screenType", out var screen) || screen.ValueKind != JsonValueKind.String || !ScreenTypeJsonConverter.WireValues.Contains(screen.GetString()!, StringComparer.Ordinal))
            errors.Add("screenType 값이 올바르지 않습니다.");
        if (!root.TryGetProperty("confidence", out var confidence) || !confidence.TryGetDouble(out var score) || score is < 0 or > 1)
            errors.Add("confidence는 0에서 1 사이여야 합니다.");
        if (!root.TryGetProperty("immediateActions", out var actions) || actions.ValueKind != JsonValueKind.Array)
            errors.Add("immediateActions는 배열이어야 합니다.");
        else
        {
            if (actions.GetArrayLength() > 3) errors.Add("immediateActions는 최대 3개입니다.");
            foreach (var action in actions.EnumerateArray())
            {
                if (action.ValueKind != JsonValueKind.Object || action.EnumerateObject().Any(p => p.Name is not ("action" or "reason"))) errors.Add("immediateActions 항목 형식이 올바르지 않습니다.");
                ValidateString(action, "action", errors); ValidateString(action, "reason", errors);
            }
        }
        if (errors.Count != 0) return null;
        try { return JsonSerializer.Deserialize<AnalysisResponse>(root.GetRawText(), AdvisorJson.Options); }
        catch (JsonException ex) { errors.Add(ex.Message); return null; }
    }

    private static ChatResponse? ValidateChat(JsonElement root, List<string> errors)
    {
        ValidateString(root, "message", errors); ValidateString(root, "stateUpdate", errors); ValidateNullableString(root, "needsAnotherScreen", errors);
        if (errors.Count != 0) return null;
        try { return JsonSerializer.Deserialize<ChatResponse>(root.GetRawText(), AdvisorJson.Options); }
        catch (JsonException ex) { errors.Add(ex.Message); return null; }
    }

    private static void ValidateString(JsonElement root, string name, List<string> errors)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || value.GetString()!.Length > MaxString)
            errors.Add($"{name}은 {MaxString}자 이하 문자열이어야 합니다.");
    }

    private static void ValidateNullableString(JsonElement root, string name, List<string> errors)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) || (value.ValueKind == JsonValueKind.String && value.GetString()!.Length > MaxString))
            errors.Add($"{name}은 null 또는 {MaxString}자 이하 문자열이어야 합니다.");
    }

    private static void ValidateStringArray(JsonElement root, string name, List<string> errors)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || item.GetString()!.Length > MaxString))
            errors.Add($"{name}은 {MaxString}자 이하 문자열 배열이어야 합니다.");
    }

    private static string ExtractJson(string raw)
    {
        var trimmed = raw.Trim();
        var match = JsonFence().Match(trimmed);
        return match.Success ? match.Groups[1].Value.Trim() : trimmed;
    }

    private static string Sanitize(string raw) => new(raw.Where(c => c is '\r' or '\n' or '\t' || !char.IsControl(c)).Take(MaxRaw).ToArray());

    [GeneratedRegex("^```json\\s*([\\s\\S]*?)\\s*```$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonFence();
}
