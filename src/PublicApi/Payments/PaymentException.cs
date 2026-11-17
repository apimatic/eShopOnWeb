using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// A payment failure that is safe to surface to an API caller. Provider internals and card data
/// never travel on this type.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(int statusCode, string message, string? providerCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public int StatusCode { get; }
    public string? ProviderCode { get; }
}
