using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingException : Exception
{
    public BillingException(string message, HttpStatusCode statusCode = HttpStatusCode.BadGateway)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingException(string message, Exception innerException, HttpStatusCode statusCode = HttpStatusCode.BadGateway)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message)
        : base(message, HttpStatusCode.ServiceUnavailable)
    {
    }
}

public class PlanNotFoundException : BillingException
{
    public PlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found.", HttpStatusCode.NotFound)
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
