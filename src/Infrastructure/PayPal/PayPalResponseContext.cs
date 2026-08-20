using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Captures the HTTP status of the last PayPal response on the current async flow. The SDK's typed-error
/// parsing (and a drifted-body <see cref="System.Text.Json.JsonException"/>) can otherwise discard the HTTP
/// status before the gateway sees it; recording it out-of-band via a <see cref="StatusCapturingHandler"/>
/// lets the gateway map a provider 4xx back to a client error and detect a stale authorization on capture.
/// </summary>
public static class PayPalResponseContext
{
    private static readonly AsyncLocal<StatusHolder?> Current = new();

    /// <summary>Opens a fresh capture scope for one gateway operation. The holder flows down into the handler.</summary>
    public static IDisposable BeginScope()
    {
        var holder = new StatusHolder();
        Current.Value = holder;
        return holder;
    }

    /// <summary>Called by the handler with each response's status code.</summary>
    public static void RecordStatus(int statusCode)
    {
        var holder = Current.Value;
        if (holder is not null)
        {
            holder.StatusCode = statusCode;
        }
    }

    /// <summary>Called by the handler with the raw body of a non-success response, for diagnostics.</summary>
    public static void RecordErrorBody(string body)
    {
        var holder = Current.Value;
        if (holder is not null)
        {
            holder.ErrorBody = body;
        }
    }

    /// <summary>The status code of the most recent PayPal response in the current scope, if any.</summary>
    public static int? CurrentStatusCode => Current.Value?.StatusCode;

    /// <summary>The raw body of the most recent non-success PayPal response in the current scope, if any.</summary>
    public static string? CurrentErrorBody => Current.Value?.ErrorBody;

    private sealed class StatusHolder : IDisposable
    {
        public int? StatusCode;
        public string? ErrorBody;
        public void Dispose() => Current.Value = null;
    }
}
