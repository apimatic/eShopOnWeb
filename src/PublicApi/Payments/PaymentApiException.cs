using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentApiException : Exception
{
    public PaymentApiException(int statusCode, string code, string message,
        IReadOnlyDictionary<string, object?>? extensions = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Extensions = extensions;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public IReadOnlyDictionary<string, object?>? Extensions { get; }
}
