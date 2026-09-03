using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Core.Response;

namespace PayPalServerSdk.Core.ErrorResponse;

public abstract class ApiError
{
    private readonly Optional<RawError> _rawError;

    internal ApiError(Optional<RawError> rawError)
    {
        _rawError = rawError;
    }

    public bool TryGetRawError(out RawError error) => _rawError.TryGetValue(out error);

    private static async Task<TSelf> From<TBody, TSelf>(
        IResponse<TBody> parser,
        Func<TBody, TSelf> wrap,
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        wrap(await parser.Map(response, cancellationToken).ConfigureAwait(false));

    protected static Parse<TBody> FromJson<TBody>(
        HttpResponseMessage response, CancellationToken cancellationToken) =>
        new(JsonResponse.Create<TBody>(), response, cancellationToken);

    protected static Parse<TBody> FromScalar<TBody>(
        HttpResponseMessage response, CancellationToken cancellationToken,
        Func<string, TBody> read) =>
        new(PlainTextResponse.Create(read), response, cancellationToken);

    protected static Parse<RawError> FromRawBody(
        HttpResponseMessage response, CancellationToken cancellationToken) =>
        new(RawErrorBodyResponse.Instance, response, cancellationToken);

    protected static Parse<ErrorByteContent> FromBytes(
        HttpResponseMessage response, CancellationToken cancellationToken) =>
        new(ErrorByteResponse.Instance, response, cancellationToken);

    protected readonly struct Parse<TBody>
    {
        private readonly IResponse<TBody> _parser;
        private readonly HttpResponseMessage _response;
        private readonly CancellationToken _cancellationToken;

        internal Parse(IResponse<TBody> parser, HttpResponseMessage response, CancellationToken cancellationToken)
        {
            _parser = parser;
            _response = response;
            _cancellationToken = cancellationToken;
        }

        public Task<TSelf> As<TSelf>(Func<TBody, TSelf> wrap) =>
            From(_parser, wrap, _response, _cancellationToken);
    }
}
