using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the country for the sender Id
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Status>))]
public sealed record Status : StringEnum<Status>
{
    private Status(string value) : base(value)
    {
    }

    public static readonly Status Live = new("LIVE");

    public static readonly Status NotLive = new("NOT_LIVE");

    public static Status FromValue(string value) => FromValueCore(value);
}
