using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Opt-in diagnostic handler (enabled only when the environment variable <c>VISA_WIRE_LOG</c> is "true").
/// Logs the outbound method/URI and the response status and body to the console — never the request's
/// signing headers or the shared secret. Off by default; intended for first-run verification of a new call.
/// </summary>
public sealed class VisaWireLogHandler : DelegatingHandler
{
    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("VISA_WIRE_LOG"), "true", StringComparison.OrdinalIgnoreCase);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[visa] --> {request.Method} {request.RequestUri}");
        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"[visa] req body: {body}");
        }

        var response = await base.SendAsync(request, cancellationToken);

        Console.WriteLine($"[visa] <-- {(int)response.StatusCode} {request.Method} {request.RequestUri}");
        if (response.Content is not null)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"[visa] resp body: {body}");
        }
        return response;
    }
}
