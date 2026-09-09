using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// all: Will clear all products, customers, and related subscriptions from the site. customers: Will clear only customers and related subscriptions (leaving the products untouched) for the site. Revenue will also be reset to 0.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CleanupScope>))]
public sealed record CleanupScope : StringEnum<CleanupScope>
{
    private CleanupScope(string value) : base(value)
    {
    }

    public static readonly CleanupScope All = new("all");

    public static readonly CleanupScope Customers = new("customers");

    public static CleanupScope FromValue(string value) => FromValueCore(value);
}
