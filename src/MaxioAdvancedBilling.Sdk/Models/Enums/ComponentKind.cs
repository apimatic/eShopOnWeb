using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// A handle for the component type
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ComponentKind>))]
public sealed record ComponentKind : StringEnum<ComponentKind>
{
    private ComponentKind(string value) : base(value)
    {
    }

    public static readonly ComponentKind MeteredComponent = new("metered_component");

    public static readonly ComponentKind QuantityBasedComponent = new("quantity_based_component");

    public static readonly ComponentKind OnOffComponent = new("on_off_component");

    public static readonly ComponentKind PrepaidUsageComponent = new("prepaid_usage_component");

    public static readonly ComponentKind EventBasedComponent = new("event_based_component");

    public static ComponentKind FromValue(string value) => FromValueCore(value);
}
