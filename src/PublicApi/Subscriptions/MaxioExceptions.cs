using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message) { }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode) : base("Maxio Advanced Billing did not accept the request.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
