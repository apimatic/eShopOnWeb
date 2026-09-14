using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class MaxioTransportException : Exception
{
    public MaxioTransportException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class MaxioContractException : Exception
{
    public MaxioContractException(string message) : base(message) { }
}
