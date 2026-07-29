using System;
using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a customer's subscription.</summary>
public class CustomerSubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public string Currency { get; set; } = "USD";
    public string FormattedPrice { get; set; } = string.Empty;

    /// <summary>The next scheduled billing date (end of the current period).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }

    public static CustomerSubscriptionDto FromDomain(CustomerSubscription s) => new()
    {
        Id = s.Id,
        State = s.State,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        Price = s.Price,
        PriceInCents = s.PriceInCents,
        Currency = s.Currency,
        FormattedPrice = FormatPrice(s.Price, s.Currency),
        NextBillingAt = s.NextBillingAt,
        NextAssessmentAt = s.NextAssessmentAt,
        CurrentPeriodStartedAt = s.CurrentPeriodStartedAt,
        CreatedAt = s.CreatedAt,
        PaymentCollectionMethod = s.PaymentCollectionMethod
    };

    private static string FormatPrice(decimal price, string currency)
    {
        var amount = price.ToString("0.00", CultureInfo.InvariantCulture);
        var symbol = string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase) ? "$" : string.Empty;
        return $"{symbol}{amount} {currency}".Trim();
    }
}
