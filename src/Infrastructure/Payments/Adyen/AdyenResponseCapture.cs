using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AdyenApIs.Core;
using AdyenApIs.Core.Hooks;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Adyen;

/// <summary>
/// Captures Adyen's response exactly as it arrived on the wire — status and body, including fields the SDK's
/// models do not declare — for the support record. One instance per SDK call, attached as a per-call hook.
/// </summary>
internal sealed class AdyenResponseCapture
{
    public AdyenResponseCapture()
    {
        RequestOptions = new RequestOptions { Hooks = [SdkHook.OnResponse(CaptureAsync)] };
    }

    public RequestOptions RequestOptions { get; }

    public int? StatusCode { get; private set; }

    public string? Body { get; private set; }

    private async ValueTask CaptureAsync(HttpResponseMessage response, HookContext context, CancellationToken cancellationToken)
    {
        StatusCode = (int)response.StatusCode;
        if (response.Content is null)
            return;

        try
        {
            // Buffer first: the SDK reads the same content after this hook returns.
            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
            Body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.IO.IOException)
        {
            // The SDK hits the same failure when it reads the body and reports the call's outcome as unknown.
            Body = null;
        }
    }
}
