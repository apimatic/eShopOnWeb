using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised instead of letting a guarded write go out on the wire a second time. It deliberately does
/// <em>not</em> derive from <see cref="HttpRequestException"/>: that is the type the SDK's retry pipeline
/// treats as a transient transport failure, so a refusal expressed that way would itself be retried.
/// </summary>
public sealed class DuplicateSendBlockedException : Exception
{
    public DuplicateSendBlockedException(string operation)
        : base($"A second network send of the write '{operation}' was blocked. The first send may already have taken effect.")
    {
        Operation = operation;
    }

    public string Operation { get; }
}

/// <summary>
/// Ambient budget of "one network send" for a single logical write.
/// </summary>
/// <remarks>
/// The SDK retries an <see cref="HttpRequestException"/> on <em>every</em> verb - a connection reset thrown
/// after the bytes reached Maxio is indistinguishable from one thrown before - and retries cannot be
/// switched off (the pipeline rejects a retry count below one). Without this guard a single
/// <c>POST /api/subscriptions</c> could enroll the same shopper twice without our code ever looping.
///
/// The claim is intentionally <em>not</em> stored on the <see cref="HttpRequestMessage"/>: the SDK builds a
/// fresh request object for every attempt, so a marker kept there is gone by the time the retry runs. An
/// <see cref="AsyncLocal{T}"/> scope opened around the call flows into the handler on every attempt
/// instead, and is released when the scope is disposed - so a blocked send can never become a permanent
/// refusal for later requests.
/// </remarks>
public sealed class MaxioWriteOnceScope : IDisposable
{
    private static readonly AsyncLocal<MaxioWriteOnceScope?> Ambient = new();

    private readonly MaxioWriteOnceScope? _previous;
    private int _claimed;
    private bool _disposed;

    public MaxioWriteOnceScope(string operation)
    {
        Operation = operation;
        _previous = Ambient.Value;
        Ambient.Value = this;
    }

    internal static MaxioWriteOnceScope? Current => Ambient.Value;

    public string Operation { get; }

    /// <summary>True once a write for this scope has been handed to the network.</summary>
    public bool WasSent => Volatile.Read(ref _claimed) != 0;

    /// <summary>Claims the single permitted send. Returns false for every subsequent attempt.</summary>
    internal bool TryClaimSend() => Interlocked.Exchange(ref _claimed, 1) == 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Ambient.Value = _previous;
    }
}

/// <summary>
/// Enforces <see cref="MaxioWriteOnceScope"/> on the outbound pipeline. Sits below the SDK's retry
/// pipeline, so it sees every attempt individually.
/// </summary>
public sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    private readonly ILogger<MaxioWriteOnceHandler> _logger;

    public MaxioWriteOnceHandler(ILogger<MaxioWriteOnceHandler> logger)
    {
        _logger = logger;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = MaxioWriteOnceScope.Current;

        if (scope is not null && IsMutating(request.Method) && !scope.TryClaimSend())
        {
            // Count the send before it leaves, not after it succeeds: a request that failed on the way
            // out may still have been received, so the only safe reading is "this may already have
            // taken effect".
            _logger.LogWarning(
                "Blocked a repeat network send of Maxio write {Operation} ({Method} {Uri}).",
                scope.Operation,
                request.Method,
                request.RequestUri);

            throw new DuplicateSendBlockedException(scope.Operation);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsMutating(HttpMethod method) =>
        method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Patch || method == HttpMethod.Delete;
}
