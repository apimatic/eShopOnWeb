using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SubscriptionGroupsListInclude>))]
public sealed record SubscriptionGroupsListInclude : StringEnum<SubscriptionGroupsListInclude>
{
    private SubscriptionGroupsListInclude(string value) : base(value)
    {
    }

    public static readonly SubscriptionGroupsListInclude AccountBalances = new("account_balances");

    public static SubscriptionGroupsListInclude FromValue(string value) => FromValueCore(value);
}
