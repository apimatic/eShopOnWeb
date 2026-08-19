using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingException : Exception
{
    public BillingException(string message, HttpStatusCode statusCode = HttpStatusCode.InternalServerError)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public class BillingValidationException : BillingException
{
    public BillingValidationException(string message)
        : base(message, HttpStatusCode.BadRequest)
    {
    }
}

public class BillingNotFoundException : BillingException
{
    public BillingNotFoundException(string message)
        : base(message, HttpStatusCode.NotFound)
    {
    }
}

public class BillingConflictException : BillingException
{
    public BillingConflictException(string message)
        : base(message, HttpStatusCode.Conflict)
    {
    }
}

public class BillingRateLimitedException : BillingException
{
    public BillingRateLimitedException(string message)
        : base(message, (HttpStatusCode)429)
    {
    }
}

public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message)
        : base(message, HttpStatusCode.ServiceUnavailable)
    {
    }
}
