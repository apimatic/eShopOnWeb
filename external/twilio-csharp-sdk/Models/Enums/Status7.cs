using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The state of the Conversation.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Status7>))]
public sealed record Status7 : StringEnum<Status7>
{
    private Status7(string value) : base(value)
    {
    }

    public static readonly Status7 Active = new("ACTIVE");

    public static readonly Status7 Inactive = new("INACTIVE");

    public static readonly Status7 Closed = new("CLOSED");

    public static Status7 FromValue(string value) => FromValueCore(value);
}
