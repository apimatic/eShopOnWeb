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

    private static SearchBalancesError AsFallback(RawError value) => new(default, Optional<RawError>.Some(value));

    public bool TryGetDefaultError(out DefaultError value) => _defaultErrorValue.TryGetValue(out value);

    private static Task<SearchBalancesError> Create(FailedResponse response) =>
        response.StatusCode switch
        {
            400 or 403 or 500 => response.Json<DefaultError>().As(AsDefaultError),
            _ => response.RawBody().As(AsFallback)
        };

    internal static ApiErrorResponse<SearchBalancesError> Response { get; } = new(Create);
}
