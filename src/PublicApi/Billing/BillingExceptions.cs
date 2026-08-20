using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class BillingException : Exception
{
    public BillingException(HttpStatusCode statusCode, string safeMessage, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class BillingValidationException : BillingException
{
    public BillingValidationException(string message)
        : base(HttpStatusCode.UnprocessableEntity, message)
    {
    }
}

public sealed class BillingConflictException : BillingException
{
    public BillingConflictException(string message)
        : base(HttpStatusCode.Conflict, message)
    {
    }
}

public sealed class MaxioProviderException : BillingException
{
    public MaxioProviderException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(statusCode, message, innerException)
    {
    }
}
