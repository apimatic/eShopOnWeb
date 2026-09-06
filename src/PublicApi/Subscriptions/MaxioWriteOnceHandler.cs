using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Stops the SDK retry pipeline from re-sending a POST after a transport failure.
/// The SDK may retry HttpRequestException failures on every HTTP verb.
/// </summary>
internal sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<CallScope?> CurrentScope = new();

    internal static IDisposable BeginScope()
    {
        var prior = CurrentScope.Value;
        CurrentScope.Value = new CallScope();
        return new Scope(prior);
    }

    internal static int? LastResponseStatusCode => CurrentScope.Value?.LastResponseStatusCode;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = CurrentScope.Value;
        if (scope is not null && request.Method == HttpMethod.Post)
        {
            var key = $"{request.Method}:{request.RequestUri}";
            if (!scope.SentWrites.Add(key))
            {
                throw new MaxioWriteRetryBlockedException();
            }
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (scope is not null)
        {
            scope.LastResponseStatusCode = (int)response.StatusCode;
        }

        return response;
    }

    private sealed class CallScope
    {
        public HashSet<string> SentWrites { get; } = new(StringComparer.Ordinal);
        public int? LastResponseStatusCode { get; set; }
    }

    private sealed class Scope : IDisposable
    {
        private readonly CallScope? _prior;

        public Scope(CallScope? prior) => _prior = prior;

        public void Dispose() => CurrentScope.Value = _prior;
    }
}
