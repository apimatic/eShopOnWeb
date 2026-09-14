using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public class MaxioApiException : Exception
{
    public MaxioApiException(string message, int? statusCode = null, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public int? StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }
}
