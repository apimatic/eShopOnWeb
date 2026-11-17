using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation failure that carries the HTTP status the API should
/// surface. The message is written for the operator/shopper to act on.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string message, string? code = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string? Code { get; }
}

public class PaymentNotFoundException : PaymentOperationException
{
    public PaymentNotFoundException(string message)
        : base(404, message, "NOT_FOUND")
    {
    }
}

public class PaymentForbiddenException : PaymentOperationException
{
    public PaymentForbiddenException(string message)
        : base(403, message, "FORBIDDEN")
    {
    }
}

public class PaymentConflictException : PaymentOperationException
{
    public PaymentConflictException(string message, string? code = null)
        : base(409, message, code ?? "CONFLICT")
    {
    }
}

public class PaymentValidationException : PaymentOperationException
{
    public PaymentValidationException(string message, string? code = null)
        : base(400, message, code ?? "VALIDATION_ERROR")
    {
    }
}
