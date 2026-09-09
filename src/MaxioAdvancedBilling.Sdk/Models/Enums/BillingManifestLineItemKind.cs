using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// A handle for the billing manifest line item kind
/// </summary>
[JsonConverter(typeof(StringEnumConverter<BillingManifestLineItemKind>))]
public sealed record BillingManifestLineItemKind : StringEnum<BillingManifestLineItemKind>
{
    private BillingManifestLineItemKind(string value) : base(value)
    {
    }

    public static readonly BillingManifestLineItemKind Baseline = new("baseline");

    public static readonly BillingManifestLineItemKind Initial = new("initial");

    public static readonly BillingManifestLineItemKind Trial = new("trial");

    public static readonly BillingManifestLineItemKind Coupon = new("coupon");

    public static readonly BillingManifestLineItemKind Component = new("component");

    public static readonly BillingManifestLineItemKind Tax = new("tax");

    public static BillingManifestLineItemKind FromValue(string value) => FromValueCore(value);
}
