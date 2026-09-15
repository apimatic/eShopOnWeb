using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentApplicationException : Exception
{
    public PaymentApplicationException(int statusCode, string title, string detail) : base(detail)
    {
        StatusCode = statusCode;
        Title = title;
    }

    public int StatusCode { get; }
    public string Title { get; }
}

public sealed class PayPalProviderException : Exception
{
    public PayPalProviderException(int statusCode, string message, string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        DebugId = debugId;
    }

    public int StatusCode { get; }
    public string? DebugId { get; }
}
