using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<TransferEnumTransferStatus>))]
public sealed record TransferEnumTransferStatus : StringEnum<TransferEnumTransferStatus>
{
    private TransferEnumTransferStatus(string value) : base(value)
    {
    }

    public static readonly TransferEnumTransferStatus Active = new("active");

    public static readonly TransferEnumTransferStatus Failed = new("failed");

    public static readonly TransferEnumTransferStatus Completed = new("completed");

    public static TransferEnumTransferStatus FromValue(string value) => FromValueCore(value);
}
