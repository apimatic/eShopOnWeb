using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentGatewayException : Exception
{
    public int StatusCode { get; }
    public string? DebugId { get; }
    public bool IsChallengeRequired { get; }

    public PaymentGatewayException(
        string message,
        int statusCode = 502,
        string? debugId = null,
        bool isChallengeRequired = false,
        Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        IsChallengeRequired = isChallengeRequired;
    }
}
