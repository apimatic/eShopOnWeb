using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CommerceException : Exception
{
    public CommerceException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
