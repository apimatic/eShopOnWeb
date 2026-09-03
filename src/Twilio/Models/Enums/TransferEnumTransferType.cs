using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<TransferEnumTransferType>))]
public sealed record TransferEnumTransferType : StringEnum<TransferEnumTransferType>
{
    private TransferEnumTransferType(string value) : base(value)
    {
    }

    public static readonly TransferEnumTransferType Warm = new("warm");

    public static readonly TransferEnumTransferType Cold = new("cold");

    public static readonly TransferEnumTransferType External = new("external");

    public static TransferEnumTransferType FromValue(string value) => FromValueCore(value);
}
