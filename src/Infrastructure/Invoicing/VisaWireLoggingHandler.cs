using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Opt-in wire diagnostics for the Visa integration (enabled by <c>Visa:LogRequests</c>). Logs the
/// method, path and response status of every provider call, and — on a non-success response — the
/// provider's error body, which is the fastest way to diagnose a rejected request. It never logs
/// request headers (so the signature and credentials are never written) and leaves the success path
/// untouched.
/// </summary>
public sealed class VisaWireLoggingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);

        Console.WriteLine($"[Visa] --> {request.Method} {request.RequestUri?.PathAndQuery}");
        Console.WriteLine($"[Visa] <-- {(int)response.StatusCode} {response.StatusCode}");

        if (!response.IsSuccessStatusCode && response.Content is not null)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            Console.WriteLine($"[Visa] error body: {body}");

            // Rebuild the content so the SDK can still read it to construct its typed error.
            var replacement = new StringContent(body, Encoding.UTF8);
            if (response.Content.Headers.ContentType is not null)
            {
                replacement.Headers.ContentType = response.Content.Headers.ContentType;
            }

            response.Content = replacement;
        }

        return response;
    }
}
