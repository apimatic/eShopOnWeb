using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PayPalServerSdk.Core.Response;

internal sealed class JsonResponse<TResponse> : IResponse<TResponse>
{
    private readonly JsonSerializerOptions? _options;

    public JsonResponse(JsonConverter? jsonConverter) =>
        _options = jsonConverter is null
            ? null
            : new JsonSerializerOptions { Converters = { jsonConverter } };

    public async ValueTask<TResponse> Map(HttpResponseMessage httpResponseMessage, CancellationToken cancellationToken)
    {
        using (httpResponseMessage)
        {
#if NET6_0_OR_GREATER
            var responseStream = await httpResponseMessage.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
            var responseStream = await httpResponseMessage.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
            if (_options is null) return (await JsonSerializer.DeserializeAsync<TResponse>(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false))!;
            return (await JsonSerializer.DeserializeAsync<TResponse>(responseStream, _options, cancellationToken).ConfigureAwait(false))!;
        }
    }
}

internal static class JsonResponse
{
    public static JsonResponse<TResponse> Create<TResponse>(JsonConverter? converter = null) => new(converter);
}