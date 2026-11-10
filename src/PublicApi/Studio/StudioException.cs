using System;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>A request the studio integration cannot carry out; carries the HTTP status and reason to answer with.</summary>
public class StudioException : Exception
{
    public int StatusCode { get; }

    public StudioException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
