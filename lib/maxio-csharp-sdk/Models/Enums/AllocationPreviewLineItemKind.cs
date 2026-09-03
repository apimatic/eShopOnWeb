using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

/// <summary>
/// A handle for the line item kind for allocation preview
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AllocationPreviewLineItemKind>))]
public sealed record AllocationPreviewLineItemKind : StringEnum<AllocationPreviewLineItemKind>
{
    private AllocationPreviewLineItemKind(string value) : base(value)
    {
    }

    public static readonly AllocationPreviewLineItemKind QuantityBasedComponent = new("quantity_based_component");

    public static readonly AllocationPreviewLineItemKind OnOffComponent = new("on_off_component");

    public static readonly AllocationPreviewLineItemKind Coupon = new("coupon");

    public static readonly AllocationPreviewLineItemKind Tax = new("tax");

    public static AllocationPreviewLineItemKind FromValue(string value) => FromValueCore(value);
}
