using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A non-success response from the Finix API. The message is Finix's own error message
/// (from the genericError model in the spec); request bodies are never included.
/// </summary>
public class FinixApiException : Exception
{
    public FinixApiException(int statusCode, string? code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string? Code { get; }
}
