using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Status2>))]
public sealed record Status2 : StringEnum<Status2>
{
    private Status2(string value) : base(value)
    {
    }

    public static readonly Status2 Queued = new("queued");

    public static readonly Status2 Running = new("running");

    public static readonly Status2 Completed = new("completed");

    public static readonly Status2 Failed = new("failed");

    public static readonly Status2 Partial = new("partial");

    public static readonly Status2 SkippedOverlap = new("skipped_overlap");

    public static Status2 FromValue(string value) => FromValueCore(value);
}
