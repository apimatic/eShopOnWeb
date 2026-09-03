using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Conversation status.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Status3>))]
public sealed record Status3 : StringEnum<Status3>
{
    private Status3(string value) : base(value)
    {
    }

    public static readonly Status3 Active = new("ACTIVE");

    public static readonly Status3 Inactive = new("INACTIVE");

    public static readonly Status3 Closed = new("CLOSED");

    public static Status3 FromValue(string value) => FromValueCore(value);
}
