using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// A handle for the line item transaction type
/// </summary>
[JsonConverter(typeof(StringEnumConverter<LineItemTransactionType>))]
public sealed record LineItemTransactionType : StringEnum<LineItemTransactionType>
{
    private LineItemTransactionType(string value) : base(value)
    {
    }

    public static readonly LineItemTransactionType Charge = new("charge");

    public static readonly LineItemTransactionType Credit = new("credit");

    public static readonly LineItemTransactionType Adjustment = new("adjustment");

    public static readonly LineItemTransactionType Payment = new("payment");

    public static readonly LineItemTransactionType Refund = new("refund");

    public static readonly LineItemTransactionType InfoTransaction = new("info_transaction");

    public static readonly LineItemTransactionType PaymentAuthorization = new("payment_authorization");

    public static LineItemTransactionType FromValue(string value) => FromValueCore(value);
}
