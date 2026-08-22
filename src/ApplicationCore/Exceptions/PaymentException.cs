using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message, int statusCode = 400, string? issue = null)
        : base(message)
    {
        StatusCode = statusCode;
        Issue = issue;
    }

    public int StatusCode { get; }
    public string? Issue { get; }
}

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message)
        : base(message, statusCode: 422, issue: "PAYER_ACTION_REQUIRED")
    {
    }
}
