using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentDomainException : Exception
{
    public PaymentDomainException(int statusCode, string message) : base(message) => StatusCode = statusCode;
    public int StatusCode { get; }
}

public sealed class PayPalProviderException : Exception
{
    public PayPalProviderException(string message, string? code = null, string? debugId = null,
        Exception? innerException = null) : base(message, innerException)
    {
        Code = code;
        DebugId = debugId;
    }

    public string? Code { get; }
    public string? DebugId { get; }
}

public sealed class PayPalPayerActionRequiredException : Exception
{
    public PayPalPayerActionRequiredException(string providerOrderId)
        : base("PayPal requires browser approval for this card. No approval round-trip was started.")
        => ProviderOrderId = providerOrderId;

    public string ProviderOrderId { get; }
}
