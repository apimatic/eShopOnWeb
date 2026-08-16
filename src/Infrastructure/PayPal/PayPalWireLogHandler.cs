using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Optional diagnostic handler that logs the PayPal request/response on the wire. Card number and
/// security code are redacted before anything is written — full card details never reach a log.
/// Enabled only when <c>PayPal:WireLog</c> is true.
/// </summary>
internal sealed class PayPalWireLogHandler : DelegatingHandler
{
    private static readonly Regex Sensitive = new(
        "\"(number|security_code)\"\\s*:\\s*\"[^\"]*\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string requestBody = string.Empty;
        if (request.Content is not null)
        {
            try { requestBody = Redact(await request.Content.ReadAsStringAsync(cancellationToken)); }
            catch { requestBody = "<unreadable>"; }
        }

        Console.WriteLine($"[PAYPAL] --> {request.Method} {request.RequestUri}");
        if (requestBody.Length > 0) Console.WriteLine($"[PAYPAL] req: {requestBody}");

        var response = await base.SendAsync(request, cancellationToken);

        if (response.Content is not null)
        {
            await response.Content.LoadIntoBufferAsync();
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"[PAYPAL] <-- {(int)response.StatusCode} {request.RequestUri}");
            if (responseBody.Length > 0) Console.WriteLine($"[PAYPAL] res: {responseBody}");
        }

        return response;
    }

    private static string Redact(string body) => Sensitive.Replace(body, "\"$1\":\"***\"");
}
