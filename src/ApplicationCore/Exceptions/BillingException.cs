using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingException : Exception
{
    public BillingException(string message, int statusCode = 500, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? new[] { message };
    }

    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }
}
