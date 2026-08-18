using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }
}
