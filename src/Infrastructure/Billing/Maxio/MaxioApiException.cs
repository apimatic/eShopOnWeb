using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// A non-success response from the Billing API. Confined to this namespace: the gateway translates
/// it into the application-level billing exceptions before it can escape.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string method, string path, IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(BuildMessage(statusCode, method, path, errors), innerException)
    {
        StatusCode = statusCode;
        Method = method;
        Path = path;
        Errors = errors?.ToList() ?? new List<string>();
    }

    public int StatusCode { get; }

    public string Method { get; }

    public string Path { get; }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(int statusCode, string method, string path, IEnumerable<string>? errors)
    {
        var detail = errors is null ? null : string.Join("; ", errors);

        return string.IsNullOrWhiteSpace(detail)
            ? $"Billing API returned {statusCode} for {method} {path}."
            : $"Billing API returned {statusCode} for {method} {path}: {detail}";
    }
}
