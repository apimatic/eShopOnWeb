using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioApiException : Exception
{
    public MaxioApiException(string message, int statusCode, string? responseBody = null, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Errors = errors ?? Array.Empty<string>();
    }

    public int StatusCode { get; }
    public string? ResponseBody { get; }
    public IReadOnlyList<string> Errors { get; }
}
