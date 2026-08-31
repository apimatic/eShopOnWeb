using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Diagnostic-only handler that logs the request line, request body and response body for provider calls.
/// It is wired in only when the environment variable <c>VISA_WIRE_LOG</c> is <c>true</c>, and it never logs
/// request headers — so the HTTP Signature header and merchant id are not written out. Off by default.
/// </summary>
internal sealed class VisaWireLogHandler : DelegatingHandler
{
    public const string EnableEnvVar = "VISA_WIRE_LOG";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Console.WriteLine($"[VISA-WIRE] --> {request.Method} {request.RequestUri}");
        if (requestBody is not null)
            Console.WriteLine($"[VISA-WIRE] req body: {requestBody}");

        var response = await base.SendAsync(request, cancellationToken);

        var responseBody = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);
        Console.WriteLine($"[VISA-WIRE] <-- {(int)response.StatusCode} {request.RequestUri}");
        Console.WriteLine($"[VISA-WIRE] resp body: {responseBody}");

        return response;
    }
}
