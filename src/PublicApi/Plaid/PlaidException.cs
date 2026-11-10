using System;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>An error answered by Plaid, or a failure to reach it.</summary>
public class PlaidException : Exception
{
    public PlaidException(string message, int httpStatus = 0, string? errorType = null, string? errorCode = null,
        string? displayMessage = null, Exception? inner = null) : base(message, inner)
    {
        HttpStatus = httpStatus;
        ErrorType = errorType;
        ErrorCode = errorCode;
        DisplayMessage = displayMessage;
    }

    public int HttpStatus { get; }
    public string? ErrorType { get; }
    public string? ErrorCode { get; }
    public string? DisplayMessage { get; }

    /// <summary>True when Plaid could not be reached or failed on its side.</summary>
    public bool IsUnavailable { get; init; }
    public bool IsNotConfigured { get; init; }
}
