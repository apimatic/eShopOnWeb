using System;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>Stax answered with an error (or could not be reached).</summary>
public class StaxException : Exception
{
    /// <summary>HTTP status Stax answered with; 0 when Stax could not be reached.</summary>
    public int StaxStatusCode { get; }

    public StaxException(int staxStatusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StaxStatusCode = staxStatusCode;
    }
}
