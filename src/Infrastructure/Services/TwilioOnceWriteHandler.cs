using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Sentinel thrown when a transport retry would send a second write. Not an HttpRequestException,
/// so the SDK retry pipeline does not retry the refusal.
/// </summary>
internal sealed class TwilioDuplicateWriteException : Exception
{
    public TwilioDuplicateWriteException()
        : base("A duplicate write was blocked before it reached the provider.")
    {
    }
}

internal sealed class TwilioOnceWriteHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> Current = new();

    public static IDisposable BeginWrite()
    {
        var scope = new WriteScope();
        Current.Value = scope;
        return scope;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isWrite = request.Method == HttpMethod.Post
            || request.Method == HttpMethod.Put
            || request.Method == HttpMethod.Patch
            || request.Method == HttpMethod.Delete;

        if (isWrite && Current.Value is { } scope)
        {
            if (scope.Sent)
            {
                throw new TwilioDuplicateWriteException();
            }

            scope.Sent = true;
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope : IDisposable
    {
        public bool Sent { get; set; }

        public void Dispose()
        {
            Current.Value = null;
        }
    }
}
