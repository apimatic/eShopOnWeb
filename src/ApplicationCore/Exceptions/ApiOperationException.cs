using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public sealed class ApiOperationException : Exception
{
    public ApiOperationException(int statusCode, string message) : base(message) => StatusCode = statusCode;
    public int StatusCode { get; }
}
