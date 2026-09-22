using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<TaxDestinationAddress>))]
public sealed record TaxDestinationAddress : StringEnum<TaxDestinationAddress>
{
    private TaxDestinationAddress(string value) : base(value)
    {
    }

    public static readonly TaxDestinationAddress ShippingThenBilling = new("shipping_then_billing");

    public static readonly TaxDestinationAddress BillingThenShipping = new("billing_then_shipping");

    public static readonly TaxDestinationAddress ShippingOnly = new("shipping_only");

    public static readonly TaxDestinationAddress BillingOnly = new("billing_only");

    public static TaxDestinationAddress FromValue(string value) => FromValueCore(value);
}
