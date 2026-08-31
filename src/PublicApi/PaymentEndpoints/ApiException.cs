using System;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class ApiException : Exception
{
    public ApiException(int statusCode, string title, string detail) : base(detail)
    {
        StatusCode = statusCode;
        Title = title;
    }

    public int StatusCode { get; }
    public string Title { get; }
}
