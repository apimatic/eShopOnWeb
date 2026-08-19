using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// What to do when the classifier can't be reached: <c>closed</c> blocks the request, <c>open</c> allows it.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<FailurePolicy>))]
public sealed record FailurePolicy : StringEnum<FailurePolicy>
{
    private FailurePolicy(string value) : base(value)
    {
    }

    public static readonly FailurePolicy Open = new("open");

    public static readonly FailurePolicy Closed = new("closed");

    public static FailurePolicy FromValue(string value) => FromValueCore(value);
}
