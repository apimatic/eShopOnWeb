using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The current chargeback status.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ChargebackStatus>))]
public sealed record ChargebackStatus : StringEnum<ChargebackStatus>
{
    private ChargebackStatus(string value) : base(value)
    {
    }

    public static readonly ChargebackStatus Open = new("open");

    public static readonly ChargebackStatus Lost = new("lost");

    public static readonly ChargebackStatus Won = new("won");

    public static readonly ChargebackStatus Closed = new("closed");

    public static ChargebackStatus FromValue(string value) => FromValueCore(value);
}
