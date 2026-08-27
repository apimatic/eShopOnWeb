using System;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

/// <summary>A non-success response from the PayPal API, carrying PayPal's error model.</summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string? errorName, string message, string? debugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ErrorName { get; }
    public string? DebugId { get; }

    public static string Describe(PayPalError? error, HttpStatusCode statusCode)
    {
        if (error == null)
        {
            return $"PayPal returned HTTP {(int)statusCode} ({statusCode}).";
        }

        var issues = error.Details == null
            ? null
            : string.Join("; ", error.Details
                .Select(d => string.Join(": ", new[] { d.Issue, d.Description }.Where(s => !string.IsNullOrWhiteSpace(s)))));

        var description = string.Join(" ", new[] { error.Message, issues }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return $"PayPal error {error.Name} (HTTP {(int)statusCode}): {description}";
    }
}
