using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(
        string message,
        int statusCode,
        string? providerName = null,
        string? debugId = null,
        string? issue = null,
        bool unknownOutcome = false)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderName = providerName;
        DebugId = debugId;
        Issue = issue;
        UnknownOutcome = unknownOutcome;
    }

    public int StatusCode { get; }
    public string? ProviderName { get; }
    public string? DebugId { get; }
    public string? Issue { get; }
    public bool UnknownOutcome { get; }
}
