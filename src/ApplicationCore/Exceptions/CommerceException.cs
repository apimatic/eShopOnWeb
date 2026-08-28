using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public sealed class CommerceException : Exception
{
    public CommerceException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}
