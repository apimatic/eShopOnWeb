using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Captures the last HTTP status for this async flow so a <see cref="System.Text.Json.JsonException"/>
/// raised while the SDK builds an error object can still be mapped as a 4xx rejection.
/// </summary>
internal static class MaxioHttpStatusHolder
{
    private static readonly AsyncLocal<HttpStatusCode?> Last = new();

    public static HttpStatusCode? LastStatus
    {
        get => Last.Value;
        set => Last.Value = value;
    }
}

internal sealed class MaxioHttpStatusCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        MaxioHttpStatusHolder.LastStatus = response.StatusCode;
        return response;
    }
}
