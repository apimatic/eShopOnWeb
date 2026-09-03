using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InteractionChannelAppEnumType>))]
public sealed record InteractionChannelAppEnumType : StringEnum<InteractionChannelAppEnumType>
{
    private InteractionChannelAppEnumType(string value) : base(value)
    {
    }

    public static readonly InteractionChannelAppEnumType Transcription = new("transcription");

    public static readonly InteractionChannelAppEnumType Studio = new("studio");

    public static readonly InteractionChannelAppEnumType Copilot = new("copilot");

    public static InteractionChannelAppEnumType FromValue(string value) => FromValueCore(value);
}
