using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Transport>))]
public sealed record Transport : StringEnum<Transport>
{
    private Transport(string value) : base(value)
    {
    }

    public static readonly Transport Usb = new("usb");

    public static readonly Transport Nfc = new("nfc");

    public static readonly Transport Ble = new("ble");

    public static readonly Transport SmartCard = new("smart-card");

    public static readonly Transport Internal = new("internal");

    public static readonly Transport Hybrid = new("hybrid");

    public static Transport FromValue(string value) => FromValueCore(value);
}
