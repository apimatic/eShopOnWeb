using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Determines if the message content can be stored or redacted based on privacy settings
/// </summary>
[JsonConverter(typeof(StringEnumConverter<MessageEnumContentRetention>))]
public sealed record MessageEnumContentRetention : StringEnum<MessageEnumContentRetention>
{
    private MessageEnumContentRetention(string value) : base(value)
    {
    }

    public static readonly MessageEnumContentRetention Retain = new("retain");

    public static readonly MessageEnumContentRetention Discard = new("discard");

    public static MessageEnumContentRetention FromValue(string value) => FromValueCore(value);
}
