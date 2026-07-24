using System.Text.Json;
using System.Text.Json.Serialization;

namespace PalWorldService.Shared.Palworld;

public record PalServerInfo
{
    public string? Version { get; init; }
    public string? ServerName { get; init; }
    public string? Description { get; init; }
}

public record PalServerMetrics
{
    public int CurrentPlayerNum { get; init; }
    public int MaxPlayerNum { get; init; }
    public double ServerFps { get; init; }
    public double ServerFrameTime { get; init; }
    public int Days { get; init; }

    /// <summary>API may return seconds (number) or a formatted string.</summary>
    [JsonConverter(typeof(StringOrNumberJsonConverter))]
    public string? Uptime { get; init; }
}

/// <summary>Accepts JSON string or number; numbers are treated as uptime seconds.</summary>
public sealed class StringOrNumberJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var seconds))
                    return FormatUptimeSeconds(seconds);
                return reader.GetDouble().ToString("0.##");
            case JsonTokenType.True:
            case JsonTokenType.False:
                return reader.GetBoolean().ToString();
            default:
                using (var doc = JsonDocument.ParseValue(ref reader))
                    return doc.RootElement.ToString();
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }

    public static string FormatUptimeSeconds(long seconds)
    {
        if (seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalDays >= 1)
            return $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m";
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s";
        return $"{t.Minutes}m {t.Seconds}s";
    }
}

/// <summary>
/// Palworld REST may return ints as float/double/string (e.g. ping).
/// </summary>
public sealed class FlexibleInt32JsonConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return 0;
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var i))
                    return i;
                if (reader.TryGetInt64(out var l))
                    return ClampToInt32(l);
                if (reader.TryGetDouble(out var d))
                    return ClampToInt32((long)Math.Round(d));
                break;
            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                    return 0;
                if (int.TryParse(s, out var parsed))
                    return parsed;
                if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsedD))
                    return ClampToInt32((long)Math.Round(parsedD));
                return 0;
            default:
                reader.Skip();
                return 0;
        }

        reader.Skip();
        return 0;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);

    private static int ClampToInt32(long value)
    {
        if (value > int.MaxValue) return int.MaxValue;
        if (value < int.MinValue) return int.MinValue;
        return (int)value;
    }
}

public record PalPlayer
{
    public string? Name { get; init; }
    public string? AccountName { get; init; }
    public string? PlayerId { get; init; }
    public string? PlayerUid { get; init; }
    public string? UserId { get; init; }
    public string? Ip { get; init; }

    [JsonConverter(typeof(FlexibleInt32JsonConverter))]
    public int Ping { get; init; }

    [JsonConverter(typeof(FlexibleInt32JsonConverter))]
    public int Level { get; init; }

    [JsonConverter(typeof(FlexibleInt32JsonConverter))]
    public int BuildingCount { get; init; }

    public PalPlayerLocation? Location { get; init; }
}

public record PalPlayerLocation
{
    public double X { get; init; }
    public double Y { get; init; }
}

public record ServerStatusSnapshot
{
    public string ServerId { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public bool IsOnline { get; init; }
    public PalServerInfo? Info { get; init; }
    public PalServerMetrics? Metrics { get; init; }
    public IReadOnlyList<PalPlayer> Players { get; init; } = [];
    public string? Error { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
}
