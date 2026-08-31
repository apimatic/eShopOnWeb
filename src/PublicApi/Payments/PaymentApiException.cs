using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentApiException : Exception
{
    public PaymentApiException(
        string safeMessage,
        HttpStatusCode statusCode = HttpStatusCode.BadGateway,
        string? providerDebugId = null,
        bool payerActionRequired = false,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
        ProviderDebugId = providerDebugId;
        PayerActionRequired = payerActionRequired;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ProviderDebugId { get; }
    public bool PayerActionRequired { get; }
}
