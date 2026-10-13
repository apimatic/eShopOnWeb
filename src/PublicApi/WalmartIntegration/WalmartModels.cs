using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.WalmartIntegration;

/// <summary>A SKU and the quantity Walmart currently records for it.</summary>
public readonly record struct WalmartSkuStock(string Sku, int Quantity);

/// <summary>The outcome of pushing one SKU's stock to Walmart during a sync.</summary>
public readonly record struct SkuSyncResult(string Sku, string Status)
{
    public const string Updated = "updated";
    public const string Failed = "failed";
}

/// <summary>
/// Raised at the Walmart integration boundary when a Walmart call fails. Carries the provider's HTTP
/// status when one is available (null for a transport failure/timeout) so callers can distinguish a
/// caller-fixable 4xx from a provider outage. Never carries a secret or a raw request body.
/// </summary>
public class WalmartApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public WalmartApiException(string message, HttpStatusCode? statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
