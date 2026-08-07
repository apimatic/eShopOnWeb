namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Shared constants for the named PayPal <see cref="System.Net.Http.HttpClient"/>.</summary>
public static class PayPalHttpClient
{
    /// <summary>The DI name of the PayPal HttpClient (its base address is the resolved API base URL).</summary>
    public const string Name = "PayPal";
}
