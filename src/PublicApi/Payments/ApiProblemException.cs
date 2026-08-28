using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class ApiProblemException : Exception
{
    public ApiProblemException(int statusCode, string title, string detail) : base(detail)
    {
        StatusCode = statusCode;
        Title = title;
    }

    public int StatusCode { get; }
    public string Title { get; }
}
