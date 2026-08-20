using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Records the last HTTP status seen on this async flow so a <see cref="System.Text.Json.JsonException"/>
/// can be mapped as a 4xx rejection vs an unreadable 2xx body.
/// </summary>
internal sealed class MaxioStatusCaptureHandler : DelegatingHandler
{
    private static readonly AsyncLocal<HttpStatusCode?> Last = new();

    public static HttpStatusCode? LastStatus => Last.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        Last.Value = response.StatusCode;
        return response;
    }
}

internal static class MaxioWriteOnceScope
{
    private static readonly AsyncLocal<bool> Armed = new();
    private static readonly AsyncLocal<int> SendCount = new();

    public static bool IsArmed => Armed.Value;

    public static int IncrementAndGet() => ++SendCount.Value;

    public static IDisposable Arm()
    {
        Armed.Value = true;
        SendCount.Value = 0;
        return new Reset();
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose()
        {
            Armed.Value = false;
            SendCount.Value = 0;
        }
    }
}

internal sealed class MaxioDuplicateWriteException : Exception
{
    public MaxioDuplicateWriteException()
        : base("A duplicate write was blocked before it reached the billing provider.")
    {
    }
}

/// <summary>
/// Holds POST send-count in AsyncLocal (not on HttpRequestMessage) so SDK transport retries cannot
/// deliver a second write. Throw a non-HttpRequestException sentinel so the retry pipeline does not
/// retry the refusal itself.
/// </summary>
internal sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (MaxioWriteOnceScope.IsArmed && request.Method == HttpMethod.Post)
        {
            if (MaxioWriteOnceScope.IncrementAndGet() > 1)
            {
                throw new MaxioDuplicateWriteException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
