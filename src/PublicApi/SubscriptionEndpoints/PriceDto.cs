namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A price as Maxio reports it: integer cents plus the same amount in decimal currency units.
/// </summary>
public class PriceDto
{
    /// <summary>Amount in currency units (e.g. 299.00).</summary>
    public decimal? Amount { get; set; }

    /// <summary>Amount in cents, exactly as Maxio reports it.</summary>
    public long? AmountInCents { get; set; }

    /// <summary>ISO currency code where Maxio reports one (subscriptions do; product listings do not).</summary>
    public string? Currency { get; set; }

    public static PriceDto FromCents(long? cents, string? currency) => new()
    {
        AmountInCents = cents,
        Amount = cents is long c ? c / 100m : null,
        Currency = string.IsNullOrWhiteSpace(currency) ? null : currency
    };
}
