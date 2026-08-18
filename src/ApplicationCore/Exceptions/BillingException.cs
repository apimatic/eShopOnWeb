using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingException : Exception
{
    public BillingException(string message, HttpStatusCode? statusCode = null, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public HttpStatusCode? StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }
}
