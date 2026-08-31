using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentApiException : Exception
{
    public PaymentApiException(int statusCode, string message, string? providerDebugId = null,
        bool outcomeUnknown = false, Exception? innerException = null) : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderDebugId = providerDebugId;
        OutcomeUnknown = outcomeUnknown;
    }

    public int StatusCode { get; }
    public string? ProviderDebugId { get; }
    public bool OutcomeUnknown { get; }
}
