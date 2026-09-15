using System;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class CommercePaymentException : Exception
{
    public CommercePaymentException(int statusCode, string message) : base(message) => StatusCode = statusCode;
    public int StatusCode { get; }
}
