using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CarouselActionType>))]
public sealed record CarouselActionType : StringEnum<CarouselActionType>
{
    private CarouselActionType(string value) : base(value)
    {
    }

    public static readonly CarouselActionType Url = new("URL");

    public static readonly CarouselActionType PhoneNumber = new("PHONE_NUMBER");

    public static readonly CarouselActionType QuickReply = new("QUICK_REPLY");

    public static CarouselActionType FromValue(string value) => FromValueCore(value);
}
