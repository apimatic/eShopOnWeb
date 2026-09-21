using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// A handle for the line item kind
/// </summary>
[JsonConverter(typeof(StringEnumConverter<LineItemKind>))]
public sealed record LineItemKind : StringEnum<LineItemKind>
{
    private LineItemKind(string value) : base(value)
    {
    }

    public static readonly LineItemKind Baseline = new("baseline");

    public static readonly LineItemKind Initial = new("initial");

    public static readonly LineItemKind Trial = new("trial");

    public static readonly LineItemKind QuantityBasedComponent = new("quantity_based_component");

    public static readonly LineItemKind PrepaidUsageComponent = new("prepaid_usage_component");

    public static readonly LineItemKind OnOffComponent = new("on_off_component");

    public static readonly LineItemKind MeteredComponent = new("metered_component");

    public static readonly LineItemKind EventBasedComponent = new("event_based_component");

    public static readonly LineItemKind Coupon = new("coupon");

    public static readonly LineItemKind Tax = new("tax");

    public static LineItemKind FromValue(string value) => FromValueCore(value);
}
