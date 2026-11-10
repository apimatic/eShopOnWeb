using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>An error that maps to an HTTP status and a reason for the caller.</summary>
public class StudioException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public StudioException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
