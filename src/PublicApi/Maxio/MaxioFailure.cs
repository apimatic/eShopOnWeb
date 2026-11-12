using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>A request the app cannot carry out; carries the HTTP status and a caller-safe reason.</summary>
public class MaxioFailure : Exception
{
    public int Status { get; }

    public MaxioFailure(int status, string message, Exception? inner = null) : base(message, inner)
    {
        Status = status;
    }
}
