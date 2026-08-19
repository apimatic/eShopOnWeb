using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Behavior when the classifier is unreachable: <c>closed</c> blocks (default), <c>open</c> allows.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<FailurePolicy1>))]
public sealed record FailurePolicy1 : StringEnum<FailurePolicy1>
{
    private FailurePolicy1(string value) : base(value)
    {
    }

    public static readonly FailurePolicy1 Open = new("open");

    public static readonly FailurePolicy1 Closed = new("closed");

    public static FailurePolicy1 FromValue(string value) => FromValueCore(value);
}
