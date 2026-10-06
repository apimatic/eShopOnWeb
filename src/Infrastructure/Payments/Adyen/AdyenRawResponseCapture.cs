using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AdyenApIs.Core;
using AdyenApIs.Core.Hooks;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Adyen;

/// <summary>
/// Captures the HTTP response body Adyen sent for one call, verbatim — including fields the SDK's
/// generated models do not declare (they carry no extension-data bag). Passed per call through
/// <see cref="RequestOptions.Hooks"/>, so each capture belongs to exactly one request.
/// </summary>
internal sealed class AdyenRawResponseCapture
{
    public AdyenRawResponseCapture()
    {
        RequestOptions = new RequestOptions { Hooks = [SdkHook.OnResponse(CaptureAsync)] };
    }

    public RequestOptions RequestOptions { get; }

    /// <summary>The body of the last response received, or null when none could be read.</summary>
    public string? Body { get; private set; }

    public int? StatusCode { get; private set; }

    private async ValueTask CaptureAsync(HttpResponseMessage response, HookContext context, CancellationToken cancellationToken)
    {
        // Hooks run once per attempt; the last attempt's response is the one the SDK goes on to read.
        StatusCode = (int)response.StatusCode;
        Body = null;
        try
        {
            // Buffering keeps the content re-readable, so the SDK still deserializes it after this hook.
            await response.Content.LoadIntoBufferAsync();
            Body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unreadable body is the SDK's to report (as a connection failure); the capture only observes.
        }
    }
}
