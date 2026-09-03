using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<WebviewSizeType>))]
public sealed record WebviewSizeType : StringEnum<WebviewSizeType>
{
    private WebviewSizeType(string value) : base(value)
    {
    }

    public static readonly WebviewSizeType Tall = new("TALL");

    public static readonly WebviewSizeType Full = new("FULL");

    public static readonly WebviewSizeType Half = new("HALF");

    public static readonly WebviewSizeType None = new("NONE");

    public static WebviewSizeType FromValue(string value) => FromValueCore(value);
}
