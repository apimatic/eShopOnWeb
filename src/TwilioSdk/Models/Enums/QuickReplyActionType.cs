using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<QuickReplyActionType>))]
public sealed record QuickReplyActionType : StringEnum<QuickReplyActionType>
{
    private QuickReplyActionType(string value) : base(value)
    {
    }

    public static readonly QuickReplyActionType QuickReply = new("QUICK_REPLY");

    public static QuickReplyActionType FromValue(string value) => FromValueCore(value);
}
