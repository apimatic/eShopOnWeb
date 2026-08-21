using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode? statusCode, IReadOnlyList<string> errors, Exception? innerException = null)
        : base("The Maxio Advanced Billing request failed.", innerException)
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public HttpStatusCode? StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }
}
