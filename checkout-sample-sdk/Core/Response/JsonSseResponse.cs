using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.Exceptions;

namespace PayPalServerSdk.Core.Response;

internal sealed class JsonSseResponse<TResponse> : IResponse<IAsyncEnumerable<TResponse>>
{
    private readonly JsonSerializerOptions? _options;
    private readonly byte[]? _sentinelBytes;
    private readonly TimeSpan? _idleTimeout;

    public JsonSseResponse(string? sentinel, TimeSpan? idleTimeout, JsonConverter? jsonConverter)
    {
        _sentinelBytes = sentinel is null ? null : Encoding.UTF8.GetBytes(sentinel);
        _idleTimeout = idleTimeout;
        _options = jsonConverter is null
            ? null
            : new JsonSerializerOptions { Converters = { jsonConverter } };
    }

    public ValueTask<IAsyncEnumerable<TResponse>> Map(
        HttpResponseMessage httpResponseMessage,
        CancellationToken cancellationToken)
    {
        return new ValueTask<IAsyncEnumerable<TResponse>>(Enumerate(cancellationToken));

        async IAsyncEnumerable<TResponse> Enumerate([EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var data in SseFrameReader
                               .EnumerateFrames(httpResponseMessage, _sentinelBytes, _idleTimeout, ct)
                               .ConfigureAwait(false))
            {
                TResponse value;
                try
                {
                    value = JsonSerializer.Deserialize<TResponse>(data, _options)!;
                }
                catch (JsonException ex)
                {
                    throw new SseDeserializationException(Encoding.UTF8.GetString(data), ex);
                }

                yield return value;
            }
        }
    }
}

internal static class JsonSseResponse
{
    public static JsonSseResponse<TResponse> Create<TResponse>(
        TimeSpan? idleTimeout = null,
        JsonConverter? converter = null) =>
        new(null, idleTimeout, converter);

    public static JsonSseResponse<TResponse> Create<TResponse>(
        string sentinel,
        TimeSpan? idleTimeout = null,
        JsonConverter? converter = null) =>
        new(sentinel, idleTimeout, converter);
}
