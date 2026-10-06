using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Logs every Maxio request/response (verb, URL, status — never headers, the Basic auth
/// credential lives there) and observes the HTTP status of the most recent attempt for the
/// current logical call. The SDK discards the status when a non-2xx body fails to match the
/// generated error model (a bare JsonException is thrown instead); the recorded status lets
/// the integration boundary tell an unparseable 2xx success body (5xx — outcome unknown)
/// apart from an unparseable error body (4xx — the request was rejected).
///
/// The observation is a mutable object published through an AsyncLocal when the call starts:
/// handler-side mutation flows back to the caller through the shared object, which a plain
/// AsyncLocal write inside the handler would NOT do (ExecutionContext copies are
/// copy-on-write and changes in the inner flow never propagate outward).
/// </summary>
public sealed class MaxioRequestLoggingHandler : DelegatingHandler
{
    private static readonly AsyncLocal<TransportObservation?> Current = new();

    private readonly ILogger<MaxioRequestLoggingHandler> _logger;

    public MaxioRequestLoggingHandler(ILogger<MaxioRequestLoggingHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Opens an observation for the current logical call. Concurrent calls each get their own;
    /// retries within one call update the same observation (the last attempt wins).
    /// </summary>
    public static TransportObservation Observe()
    {
        var observation = new TransportObservation(Current.Value);
        Current.Value = observation;
        return observation;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("--> {Method} {Uri}", request.Method, request.RequestUri);

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "<-- {Method} {Uri} failed before a response", request.Method, request.RequestUri);
            throw;
        }

        if (Current.Value is { } observation)
        {
            observation.RecordStatus((int)response.StatusCode);
        }

        _logger.LogDebug("<-- {StatusCode} {Method} {Uri}", (int)response.StatusCode, request.Method, request.RequestUri);
        return response;
    }

    public sealed class TransportObservation : IDisposable
    {
        private readonly TransportObservation? _previous;

        internal TransportObservation(TransportObservation? previous)
        {
            _previous = previous;
        }

        /// <summary>Status code of the most recent completed attempt in this logical call, if any.</summary>
        public int? LastStatus { get; private set; }

        public bool HasAttempt => LastStatus.HasValue;

        internal void RecordStatus(int statusCode) => LastStatus = statusCode;

        public void Dispose() => Current.Value = _previous;
    }
}
