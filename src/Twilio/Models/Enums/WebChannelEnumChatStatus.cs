using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<WebChannelEnumChatStatus>))]
public sealed record WebChannelEnumChatStatus : StringEnum<WebChannelEnumChatStatus>
{
    private WebChannelEnumChatStatus(string value) : base(value)
    {
    }

    public static readonly WebChannelEnumChatStatus Inactive = new("inactive");

    public static WebChannelEnumChatStatus FromValue(string value) => FromValueCore(value);
}
