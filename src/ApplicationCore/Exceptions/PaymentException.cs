using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment/fulfilment failure that is safe to return to an API caller.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message, int statusCode, string? providerCode = null,
        bool outcomeUnknown = false, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
        OutcomeUnknown = outcomeUnknown;
    }

    public int StatusCode { get; }
    public string? ProviderCode { get; }
    public bool OutcomeUnknown { get; }
}
