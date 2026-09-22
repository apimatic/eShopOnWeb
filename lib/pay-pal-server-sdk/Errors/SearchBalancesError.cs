using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class SearchBalancesError : ApiError
{
    private readonly Optional<DefaultError> _defaultErrorValue;

    private SearchBalancesError(Optional<DefaultError> defaultErrorValue, Optional<RawError> fallback) : base(fallback)
    {
        _defaultErrorValue = defaultErrorValue;
    }

    private static SearchBalancesError AsDefaultError(DefaultError value) =>
        new(Optional<DefaultError>.Some(value), default);

    private static SearchBalancesError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetDefaultError(out DefaultError value) => _defaultErrorValue.TryGetValue(out value);

    internal static Task<SearchBalancesError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 403 or 500 => FromJson<DefaultError>(response, ct).As(AsDefaultError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class SearchBalancesErrorResponse : IErrorResponse<SearchBalancesError>
{
    public static SearchBalancesErrorResponse Instance { get; } = new();

    private SearchBalancesErrorResponse()
    {
    }

    public Task<SearchBalancesError> Map(HttpResponseMessage response, CancellationToken ct) =>
        SearchBalancesError.Create(response, ct);
}
