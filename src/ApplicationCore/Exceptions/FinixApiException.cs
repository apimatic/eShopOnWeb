using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class FinixApiException : Exception
{
    public FinixApiException(int statusCode, string? code, string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Errors = errors ?? Array.Empty<string>();
    }

    public int StatusCode { get; }
    public string? Code { get; }
    public IReadOnlyList<string> Errors { get; }
}
