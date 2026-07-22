using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Retries transient billing-provider failures on reads, a bounded number of times with
/// exponential back-off, honouring a <c>Retry-After</c> header when the provider supplies one.
/// </summary>
/// <remarks>
/// Only safe reads are replayed. A failed write may already have been applied by the provider,
/// so it is surfaced rather than resent — resending risks double-billing or a duplicate
/// enrollment (plan UC1/UC2). Recovery from an ambiguous write is a use-case decision
/// (re-read the state), not something a transport handler may take on itself.
/// </remarks>
public class MaxioTransientFaultHandler : DelegatingHandler
{
    private static readonly HashSet<HttpStatusCode> TransientStatuses = new()
    {
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    };

    private static readonly HashSet<HttpMethod> SafeMethods = new()
    {
        HttpMethod.Get,
        HttpMethod.Head
    };

    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioTransientFaultHandler> _logger;

    public MaxioTransientFaultHandler(IOptions<MaxioSettings> settings, IAppLogger<MaxioTransientFaultHandler> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var safe = SafeMethods.Contains(request.Method);
        var maxAttempts = safe ? Math.Max(1, _settings.MaxRetryAttempts) : 1;
        var buffered = await BufferContentAsync(request, cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            var isFinalAttempt = attempt >= maxAttempts;
            using var attemptRequest = CloneRequest(request, buffered);

            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken);
            }
            catch (Exception ex) when (IsTransientTransportFault(ex, cancellationToken))
            {
                // A connection-level fault on a write may still have reached the provider.
                if (isFinalAttempt)
                {
                    throw;
                }

                _logger.LogWarning($"Billing provider transport fault on attempt {attempt} of {maxAttempts}; retrying.");
                await DelayAsync(null, attempt, cancellationToken);
                continue;
            }

            if (isFinalAttempt || !TransientStatuses.Contains(response.StatusCode))
            {
                return response;
            }

            var retryAfter = response.Headers.RetryAfter;
            response.Dispose();

            _logger.LogWarning($"Billing provider returned a retryable response on attempt {attempt} of {maxAttempts}; retrying.");
            await DelayAsync(retryAfter, attempt, cancellationToken);
        }
    }

    private static bool IsTransientTransportFault(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or IOException ||
        // A TaskCanceledException that is not the caller's cancellation is the client timeout.
        (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private Task DelayAsync(System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter, int attempt, CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromMilliseconds(Math.Max(1, _settings.RetryBaseDelayMilliseconds) * Math.Pow(2, attempt - 1));
        var delay = ResolveRetryAfter(retryAfter) ?? backoff;

        // Never let a hostile or mistaken Retry-After stall the caller indefinitely.
        var ceiling = TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds));

        return Task.Delay(delay > ceiling ? ceiling : delay, cancellationToken);
    }

    private static TimeSpan? ResolveRetryAfter(System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta.HasValue)
        {
            return retryAfter.Delta.Value;
        }

        if (retryAfter.Date.HasValue)
        {
            var delta = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        return null;
    }

    private static async Task<byte[]?> BufferContentAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request, byte[]? content)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };

        if (content is not null)
        {
            clone.Content = new ByteArrayContent(content);
            foreach (var header in request.Content!.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in (IDictionary<string, object?>)request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        return clone;
    }
}
