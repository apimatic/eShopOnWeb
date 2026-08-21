using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        HttpStatusCode statusCode,
        string code,
        string safeMessage,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        SafeMessage = safeMessage;
    }

    public HttpStatusCode StatusCode { get; }
    public string Code { get; }
    public string SafeMessage { get; }
}

public sealed class BillingProviderException : SubscriptionBillingException
{
    public BillingProviderException(
        HttpStatusCode statusCode,
        string code,
        string safeMessage,
        bool outcomeUnknown,
        Exception? innerException = null)
        : base(statusCode, code, safeMessage, innerException)
    {
        OutcomeUnknown = outcomeUnknown;
    }

    public bool OutcomeUnknown { get; }
}
