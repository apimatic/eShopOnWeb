using System;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PayByBank;

/// <summary>A Plaid error translated into an HTTP status and a reason for the caller.</summary>
public class PlaidApiException : PayByBankException
{
    public string? ErrorCode { get; }
    public string? ErrorType { get; }

    public PlaidApiException(HttpStatusCode status, string message, string? errorCode, string? errorType, Exception? inner = null)
        : base(status, message, inner)
    {
        ErrorCode = errorCode;
        ErrorType = errorType;
    }
}
