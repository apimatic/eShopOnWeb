using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public sealed class BillingException : Exception
{
    public BillingException(string code, string safeMessage, HttpStatusCode statusCode, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        Code = code;
        SafeMessage = safeMessage;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public string SafeMessage { get; }
    public HttpStatusCode StatusCode { get; }
}

internal sealed class MaxioWriteAlreadyAttemptedException : Exception
{
    public MaxioWriteAlreadyAttemptedException()
        : base("The Maxio write was blocked because this logical command has already been sent.") { }
}
