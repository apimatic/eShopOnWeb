using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// A payment failure that is safe to surface to an API caller. <see cref="StatusCode"/> is the
/// HTTP status the endpoint should return; <see cref="OutcomeUnknown"/> tells the caller the
/// provider may have acted even though we could not read the answer.
/// </summary>
public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(
        int statusCode,
        string message,
        bool outcomeUnknown = false,
        string? providerCode = null,
        string? providerLogRef = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        OutcomeUnknown = outcomeUnknown;
        ProviderCode = providerCode;
        ProviderLogRef = providerLogRef;
    }

    public int StatusCode { get; }
    public bool OutcomeUnknown { get; }
    public string? ProviderCode { get; }
    public string? ProviderLogRef { get; }
}
