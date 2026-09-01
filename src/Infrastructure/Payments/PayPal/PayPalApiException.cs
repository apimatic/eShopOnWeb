using System;
using System.Linq;
using System.Net;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal.Dto;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

/// <summary>
/// A failed PayPal API call. Carries the spec's error model (name, message, debug_id,
/// detail issues). Never carries request payloads, so no card data can leak through it.
/// </summary>
internal sealed class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, PayPalErrorResponse? error, string? rawFallback = null)
        : base(BuildMessage(error, rawFallback))
    {
        StatusCode = (int)statusCode;
        ErrorName = error?.Name;
        DebugId = error?.DebugId;
        Issues = error?.Details?.Select(d => d.Issue ?? string.Empty).Where(i => i.Length > 0).ToArray()
                 ?? Array.Empty<string>();
    }

    public int StatusCode { get; }
    public string? ErrorName { get; }
    public string? DebugId { get; }
    public string[] Issues { get; }

    private static string BuildMessage(PayPalErrorResponse? error, string? rawFallback)
    {
        if (error is null)
        {
            return string.IsNullOrEmpty(rawFallback) ? "PayPal request failed." : rawFallback;
        }
        var issues = error.Details is null
            ? string.Empty
            : string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}"));
        return string.IsNullOrEmpty(issues) ? error.Message ?? "PayPal request failed." : $"{error.Message} ({issues})";
    }
}
