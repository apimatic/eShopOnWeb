using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// The current status of the extract job
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Status4>))]
public sealed record Status4 : StringEnum<Status4>
{
    private Status4(string value) : base(value)
    {
    }

    public static readonly Status4 Completed = new("completed");

    public static readonly Status4 Processing = new("processing");

    public static readonly Status4 Failed = new("failed");

    public static readonly Status4 Cancelled = new("cancelled");

    public static Status4 FromValue(string value) => FromValueCore(value);
}
