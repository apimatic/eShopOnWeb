using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int? statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public MaxioApiException(int? statusCode, string message, IReadOnlyList<string> errors)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public int? StatusCode { get; }

    public IReadOnlyList<string> Errors { get; } = Array.Empty<string>();
}
