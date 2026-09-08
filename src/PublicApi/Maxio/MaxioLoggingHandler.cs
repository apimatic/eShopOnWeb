using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Optional request/response logging for the Maxio HTTP pipeline, enabled only when
/// <c>Maxio:LogTraffic</c> is true. Use it to verify the first run of an integration on the wire;
/// keep it disabled otherwise. Response bodies are buffered and re-wrapped so downstream parsing is
/// unaffected.
/// </summary>
public sealed class MaxioLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioLoggingHandler> _logger;

    public MaxioLoggingHandler(ILogger<MaxioLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? requestBody = null;
        if (request.Content is not null)
        {
            requestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        _logger.LogInformation("Maxio --> {Method} {Uri}{Body}",
            request.Method,
            request.RequestUri,
            string.IsNullOrEmpty(requestBody) ? string.Empty : Environment.NewLine + requestBody);

        var response = await base.SendAsync(request, cancellationToken);

        string? responseBody = null;
        if (response.Content is not null)
        {
            var originalContent = response.Content;
            var bytes = await originalContent.ReadAsByteArrayAsync(cancellationToken);
            responseBody = Encoding.UTF8.GetString(bytes);
            var bufferedContent = new ByteArrayContent(bytes);
            foreach (var header in originalContent.Headers)
            {
                bufferedContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Content = bufferedContent;
        }

        _logger.LogInformation("Maxio <-- {(int)Status} {Uri}{Body}",
            (int)response.StatusCode,
            request.RequestUri,
            string.IsNullOrEmpty(responseBody) ? string.Empty : Environment.NewLine + responseBody);

        return response;
    }
}
