using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Carries the HTTP status of the last PayPal response across the SDK call, so the error boundary can read
/// it even when a <see cref="System.Text.Json.JsonException"/> replaces the typed SDK exception while the
/// error object is being constructed (which otherwise destroys the status). The holder is created in the
/// caller's async context before the call, so the handler (a child context) mutating it flows back out.
/// </summary>
public static class PayPalResponseContext
{
    private sealed class StatusHolder
    {
        public int? StatusCode;
        public string? ErrorBody;
    }

    private static readonly AsyncLocal<StatusHolder?> Current = new();

    /// <summary>Open a scope around a single logical PayPal call. Dispose to clear it.</summary>
    public static IDisposable BeginScope()
    {
        Current.Value = new StatusHolder();
        return new Scope();
    }

    /// <summary>Record the status seen by the handler for the current scope (last attempt wins).</summary>
    public static void Record(int statusCode, string? errorBody = null)
    {
        var holder = Current.Value;
        if (holder is not null)
        {
            holder.StatusCode = statusCode;
            if (errorBody is not null)
            {
                holder.ErrorBody = errorBody;
            }
        }
    }

    /// <summary>The last HTTP status observed within the current scope, if any.</summary>
    public static int? LastStatusCode => Current.Value?.StatusCode;

    /// <summary>The raw error body of the last non-2xx response within the current scope, if captured.</summary>
    public static string? LastErrorBody => Current.Value?.ErrorBody;

    private sealed class Scope : IDisposable
    {
        public void Dispose() => Current.Value = null;
    }
}

/// <summary>Records each PayPal response's status code into <see cref="PayPalResponseContext"/>.</summary>
public sealed class StatusCapturingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        string? errorBody = null;
        if ((int)response.StatusCode >= 400 && response.Content is not null)
        {
            // Buffer so the SDK can still read the body afterwards.
            await response.Content.LoadIntoBufferAsync();
            errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        }

        PayPalResponseContext.Record((int)response.StatusCode, errorBody);
        return response;
    }
}
