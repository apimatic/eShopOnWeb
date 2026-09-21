using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SignUpOption>))]
public sealed record SignUpOption : StringEnum<SignUpOption>
{
    private SignUpOption(string value) : base(value)
    {
    }

    public static readonly SignUpOption OnlineWebForm = new("ONLINE_WEB_FORM");

    public static readonly SignUpOption Ivr = new("IVR");

    public static readonly SignUpOption Verbally = new("VERBALLY");

    public static readonly SignUpOption MobileAppOrDigitalKiosk = new("MOBILE_APP_OR_DIGITAL_KIOSK");

    public static readonly SignUpOption PaperForm = new("PAPER_FORM");

    public static readonly SignUpOption ShortcodeKeyword = new("SHORTCODE_KEYWORD");

    public static readonly SignUpOption OtherForm = new("OTHER_FORM");

    public static SignUpOption FromValue(string value) => FromValueCore(value);
}
