using System;
namespace Microsoft.eShopWeb.PublicApi.Payments;
public sealed class PaymentProviderException : Exception
{
    public int? StatusCode { get; }
    public PaymentProviderException(string message, int? statusCode = null, Exception? inner = null) : base(message, inner) => StatusCode = statusCode;
}
