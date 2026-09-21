using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Current status of the operation.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Status21>))]
public sealed record Status21 : StringEnum<Status21>
{
    private Status21(string value) : base(value)
    {
    }

    public static readonly Status21 Pending = new("PENDING");

    public static readonly Status21 Running = new("RUNNING");

    public static readonly Status21 Cancelled = new("CANCELLED");

    public static readonly Status21 Completed = new("COMPLETED");

    public static readonly Status21 Failed = new("FAILED");

    public static Status21 FromValue(string value) => FromValueCore(value);
}
