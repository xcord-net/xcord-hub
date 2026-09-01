using System.Text.Json;
using System.Text.Json.Serialization;

namespace XcordHub.Features.Admin;

/// <summary>
/// The wire shape of the spark console's app-stats contract, schema 2
/// (contracts/app-stats.md and app-stats.schema.json in the spark repository).
///
/// The schema sets additionalProperties:false at every level, so a stray field is
/// a rejected payload rather than an ignored one. It is also snake_case and needs
/// longs written as numbers, while the hub's global JSON options are camelCase
/// with a converter that writes every long as a STRING (Snowflake IDs would lose
/// precision in JavaScript otherwise). Rather than fight either, the stats
/// endpoint serializes with <see cref="Options"/> of its own.
/// </summary>
public static class StatsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // NOT a global WhenWritingNull: a cell's `v` is required and null there
        // is meaningful - it is the contract's "NOT TRACKED", rendered as an em
        // dash. Optional fields opt in individually.
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}

/// <summary>Severity values shared by alerts, posture rows and cells.</summary>
public static class StatsSeverity
{
    public const string Ok = "ok";
    public const string Info = "info";
    public const string Warn = "warn";
    public const string Crit = "crit";
}

/// <summary>Units the console formats. The app sends the raw value.</summary>
public static class StatsUnit
{
    public const string Cents = "cents";
    public const string Bytes = "bytes";
    public const string Seconds = "seconds";
    public const string Count = "count";
}

/// <summary>
/// Something that needs a person. The most important field in the payload:
/// rendered above everything else and counted on the tab's own label.
/// </summary>
public sealed record StatsAlert
{
    public required string Severity { get; init; }

    public required string Message { get; init; }

    /// <summary>Stable across releases so an alert can be suppressed or alerted on.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Count { get; init; }

    /// <summary>"Stuck for 4 minutes" and "stuck for 3 days" are different problems.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? OldestAgeS { get; init; }

    /// <summary>
    /// Opaque identifiers for the affected records - instance domains, submission
    /// ids. Never a name or an email: an operations console is not a place for
    /// personal data, and a record id is not one.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Detail { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Href { get; init; }
}

/// <summary>
/// Configuration that silently changes behaviour. Reports the EFFECTIVE state -
/// what the code will actually do - not the value someone typed into a config file.
/// </summary>
public sealed record StatsPosture
{
    public required string Label { get; init; }

    public required string State { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Value { get; init; }

    /// <summary>The consequence, in xcord's own words.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }
}

/// <summary>A figure for the top of the tab, with a label xcord chooses.</summary>
public sealed record StatsHeadline
{
    public required string Label { get; init; }

    public required object? Value { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Unit { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? State { get; init; }

    /// <summary>now, 1h, 24h, 7d, 30d, 90d, all - rolling back from generated_at.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Window { get; init; }

    /// <summary>
    /// For point-in-time figures such as MRR, which is not a trailing-window sum
    /// and is wrong if shown as one.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AsOf { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Href { get; init; }
}

/// <summary>
/// A cell that needs to stand out or needs formatting. Bare scalars are allowed
/// in a row too; this is the object form.
/// </summary>
public sealed record StatsCell
{
    /// <summary>Always written. null means NOT TRACKED, and is never 0.</summary>
    public required object? V { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? State { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Unit { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Href { get; init; }

    public static StatsCell NotTracked(string? unit = null) => new() { V = null, Unit = unit };
}

/// <summary>A titled table.</summary>
public sealed record StatsSection
{
    public required string Title { get; init; }

    /// <summary>
    /// This section could not be computed, and is rendered INSTEAD of its rows.
    /// Without it an omitted section and an empty one look identical, so "cannot
    /// see" reads as "nothing wrong".
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Columns { get; init; }

    /// <summary>Set when rows were cut. The console must never drop rows silently, and neither do we.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<IReadOnlyList<object?>>? Rows { get; init; }
}

/// <summary>The envelope. <c>schema</c> and <c>generated_at</c> are required.</summary>
public sealed record AppStatsResponse
{
    public int Schema { get; init; } = 2;

    public string App { get; init; } = "xcord";

    /// <summary>The deployed build, so it lines up against the deploy panel's digest.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }

    public required string GeneratedAt { get; init; }

    /// <summary>How stale this may be before the console marks it stale.</summary>
    public int MaxAgeSeconds { get; init; }

    /// <summary>Surfaces a stats query becoming expensive before it becomes a timeout.</summary>
    public int ComputedMs { get; init; }

    /// <summary>Empty is a positive statement - "nothing is wrong" - and differs from omitting it.</summary>
    public required IReadOnlyList<StatsAlert> Alerts { get; init; }

    public required IReadOnlyList<StatsPosture> Posture { get; init; }

    public required IReadOnlyList<StatsHeadline> Headline { get; init; }

    public required IReadOnlyList<StatsSection> Sections { get; init; }
}
