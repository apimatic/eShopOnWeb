using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Refuses a second POST/PATCH/DELETE send inside one SDK call so transport retries cannot duplicate enrollments.
/// </summary>
internal sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsWrite(request.Method) && MaxioHttpCallContext.Current is { } state)
        {
            if (state.WriteSends >= 1)
            {
                throw new MaxioWriteResendRefusedException();
            }

            state.WriteSends++;
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method)
        => method == HttpMethod.Post
           || method == HttpMethod.Put
           || method == HttpMethod.Patch
           || method == HttpMethod.Delete;
}

/// <summary>
/// Captures the last HTTP status so a <see cref="System.Text.Json.JsonException"/> can be mapped as 4xx vs 5xx.
/// </summary>
internal sealed class MaxioStatusCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (MaxioHttpCallContext.Current is { } state)
        {
            state.LastStatus = response.StatusCode;
        }

        return response;
    }
}
