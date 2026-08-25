using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Thrown when the Maxio Billing API returns a non-success status code.</summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string responseBody)
        : base($"Maxio API request failed with status {(int)statusCode} ({statusCode}): {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }
}

/// <summary>Thrown when the Maxio integration is invoked without valid configuration.</summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
