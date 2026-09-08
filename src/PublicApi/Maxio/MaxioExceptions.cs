using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when the Maxio billing subsystem cannot be used because configuration is missing
/// (Maxio:ApiKey / Maxio:Subdomain or Maxio:BaseUrl). Maps to HTTP 503 at the boundary.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }

    public MaxioConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// A Maxio/upstream failure that should surface to the API caller with a concrete HTTP status.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public MaxioApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public MaxioApiException(int statusCode, string message, Exception innerException) : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
