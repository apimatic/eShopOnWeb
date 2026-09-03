using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PayPalServerSdk.Core.Response;

internal sealed class PlainTextResponse<TResponse> : IResponse<TResponse>
{
    private readonly Func<string, TResponse> _map;

    internal PlainTextResponse(Func<string, TResponse> map) => _map = map;

    public async ValueTask<TResponse> Map(HttpResponseMessage httpResponseMessage, CancellationToken cancellationToken)
    {
        using (httpResponseMessage)
        {
#if NET6_0_OR_GREATER
            var responseString = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            var responseString = await httpResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
            return _map(responseString);
        }
    }
}

internal static class PlainTextResponse
{
    internal static PlainTextResponse<TResponse> Create<TResponse>(Func<string, TResponse> map) =>
        new(map);

    internal static PlainTextResponse<TResponse?> CreateNullable<TResponse>(Func<string, TResponse> map)
        where TResponse : struct =>
        new(body => string.IsNullOrWhiteSpace(body) ? null : map(body));

    internal static PlainTextResponse<string> CreateString() =>
        new(static s => s);

    internal static PlainTextResponse<string?> CreateNullableString() =>
        new(static body => string.IsNullOrWhiteSpace(body) ? null : body);
}
