using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
    }
}
