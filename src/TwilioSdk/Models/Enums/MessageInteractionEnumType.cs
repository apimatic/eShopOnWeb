using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The Type of Message Interaction. This value is always <c>message</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<MessageInteractionEnumType>))]
public sealed record MessageInteractionEnumType : StringEnum<MessageInteractionEnumType>
{
    private MessageInteractionEnumType(string value) : base(value)
    {
    }

    public static readonly MessageInteractionEnumType Message = new("message");

    public static readonly MessageInteractionEnumType Voice = new("voice");

    public static readonly MessageInteractionEnumType Unknown = new("unknown");

    public static MessageInteractionEnumType FromValue(string value) => FromValueCore(value);
}
