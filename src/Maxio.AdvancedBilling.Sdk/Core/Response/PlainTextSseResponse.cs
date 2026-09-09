using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MaxioAdvancedBilling.Core.Response;

internal sealed class PlainTextSseResponse<TResponse> : IResponse<IAsyncEnumerable<TResponse>>
{
    private readonly Func<string, TResponse> _map;
    private readonly byte[]? _sentinelBytes;
    private readonly TimeSpan? _idleTimeout;

    public PlainTextSseResponse(Func<string, TResponse> map, string? sentinel, TimeSpan? idleTimeout)
    {
        _map = map;
        _sentinelBytes = sentinel is null ? null : Encoding.UTF8.GetBytes(sentinel);
        _idleTimeout = idleTimeout;
    }

    public ValueTask<IAsyncEnumerable<TResponse>> Map(
        HttpResponseMessage httpResponseMessage,
        CancellationToken cancellationToken) => new(Enumerate(httpResponseMessage, cancellationToken));

    private async IAsyncEnumerable<TResponse> Enumerate(
        HttpResponseMessage httpResponseMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var data in SseFrameReader
                           .EnumerateFrames(httpResponseMessage, _sentinelBytes, _idleTimeout, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return _map(Encoding.UTF8.GetString(data));
        }
    }
}

internal static class PlainTextSseResponse
{
    public static PlainTextSseResponse<TResponse> Create<TResponse>(
        Func<string, TResponse> map, TimeSpan? idleTimeout = null) =>
        new(map, null, idleTimeout);

    public static PlainTextSseResponse<TResponse> Create<TResponse>(
        Func<string, TResponse> map, string sentinel, TimeSpan? idleTimeout = null) =>
        new(map, sentinel, idleTimeout);

    public static PlainTextSseResponse<TResponse?> CreateNullable<TResponse>(
        Func<string, TResponse> map, TimeSpan? idleTimeout = null)
        where TResponse : struct =>
        new(body => string.IsNullOrWhiteSpace(body) ? null : map(body), null, idleTimeout);

    public static PlainTextSseResponse<TResponse?> CreateNullable<TResponse>(
        Func<string, TResponse> map, string sentinel, TimeSpan? idleTimeout = null)
        where TResponse : struct =>
        new(body => string.IsNullOrWhiteSpace(body) ? null : map(body), sentinel, idleTimeout);

    public static PlainTextSseResponse<string> CreateString(TimeSpan? idleTimeout = null) =>
        new(static s => s, null, idleTimeout);

    public static PlainTextSseResponse<string> CreateString(string sentinel, TimeSpan? idleTimeout = null) =>
        new(static s => s, sentinel, idleTimeout);

    public static PlainTextSseResponse<string?> CreateNullableString(TimeSpan? idleTimeout = null) =>
        new(static body => string.IsNullOrWhiteSpace(body) ? null : body, null, idleTimeout);

    public static PlainTextSseResponse<string?> CreateNullableString(string sentinel, TimeSpan? idleTimeout = null) =>
        new(static body => string.IsNullOrWhiteSpace(body) ? null : body, sentinel, idleTimeout);
}
