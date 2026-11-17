using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when Finix rejects a request or returns an error document.
/// </summary>
public class FinixApiException : Exception
{
    public FinixApiException(
        int statusCode,
        string message,
        string? code = null,
        string? logref = null,
        string? transferId = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Logref = logref;
        TransferId = transferId;
    }

    public int StatusCode { get; }
    public string? Code { get; }
    public string? Logref { get; }
    public string? TransferId { get; }
}
