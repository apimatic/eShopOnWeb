using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment or order operation that failed for a caller-actionable reason. The status code
/// is surfaced by the PublicApi exception middleware so callers get a useful HTTP response.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string message, string? code = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string? Code { get; }
}
