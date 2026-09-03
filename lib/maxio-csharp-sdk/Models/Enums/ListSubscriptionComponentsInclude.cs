using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ListSubscriptionComponentsInclude>))]
public sealed record ListSubscriptionComponentsInclude : StringEnum<ListSubscriptionComponentsInclude>
{
    private ListSubscriptionComponentsInclude(string value) : base(value)
    {
    }

    public static readonly ListSubscriptionComponentsInclude Subscription = new("subscription");

    public static readonly ListSubscriptionComponentsInclude HistoricUsages = new("historic_usages");

    public static ListSubscriptionComponentsInclude FromValue(string value) => FromValueCore(value);
}
