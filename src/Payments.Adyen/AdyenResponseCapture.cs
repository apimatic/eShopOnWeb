using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AdyenApIs.Core;
using AdyenApIs.Core.Hooks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Payments.Adyen;

/// <summary>
/// Keeps every response body Adyen sends for one SDK call, verbatim. The SDK's response models drop fields they do
/// not declare (PaymentResponse has no extension-data bag), so the raw body is taken on the SDK's per-call hook:
/// read once here, then handed back to the SDK as an equivalent buffered content so its own parsing is unaffected.
/// </summary>
internal sealed class AdyenResponseCapture
{
    private readonly TimeProvider _time;
    private readonly List<CapturedProviderResponse> _responses = new();
    private readonly object _gate = new();

    public AdyenResponseCapture(TimeProvider time)
    {
        _time = time;
        RequestOptions = new RequestOptions { Hooks = [SdkHook.OnResponse(CaptureAsync)] };
    }

    /// <summary>Pass to the SDK call; the hook runs once per attempt.</summary>
    public RequestOptions RequestOptions { get; }

    public IReadOnlyList<CapturedProviderResponse> Responses
    {
        get { lock (_gate) return _responses.ToArray(); }
    }

    public void AddNote(string note)
    {
        lock (_gate) _responses.Add(new CapturedProviderResponse(_time.GetUtcNow(), null, null, note));
    }

    private async ValueTask CaptureAsync(HttpResponseMessage response, HookContext context, CancellationToken cancellationToken)
    {
        string? body = null;
        if (response.Content is { } original)
        {
            var bytes = await original.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var replacement = new ByteArrayContent(bytes);
            foreach (var header in original.Headers)
            {
                if (!string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            response.Content = replacement;
            original.Dispose();
            body = Encoding.UTF8.GetString(bytes);
        }

        lock (_gate) _responses.Add(new CapturedProviderResponse(_time.GetUtcNow(), (int)response.StatusCode, body, null));
    }
}
