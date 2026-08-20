using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(HttpStatusCode statusCode, string safeMessage, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
        SafeMessage = safeMessage;
    }

    public HttpStatusCode StatusCode { get; }
    public string SafeMessage { get; }
}

internal sealed class MaxioNotFoundException : Exception { }

internal sealed class MaxioValidationException : Exception
{
    public MaxioValidationException(string message, Exception? innerException = null) : base(message, innerException) { }
}

internal sealed class MaxioDependencyException : Exception
{
    public MaxioDependencyException(string message, Exception? innerException = null) : base(message, innerException) { }
}

internal sealed class MaxioUnknownOutcomeException : Exception
{
    public MaxioUnknownOutcomeException(string message, Exception? innerException = null) : base(message, innerException) { }
}
