using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentOperationException : Exception
{
    public PaymentOperationException(string code, string message, int statusCode = 400, IReadOnlyDictionary<string, object?>? details = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        Details = details;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public IReadOnlyDictionary<string, object?>? Details { get; }
}
