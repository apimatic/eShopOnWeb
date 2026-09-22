using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class CreateOrUpdateCouponCurrencyPricesError : ApiError
{
    private readonly Optional<ErrorStringMapResponse1> _errorStringMapResponse1Value;

    private CreateOrUpdateCouponCurrencyPricesError(Optional<ErrorStringMapResponse1> errorStringMapResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _errorStringMapResponse1Value = errorStringMapResponse1Value;
    }

    private static CreateOrUpdateCouponCurrencyPricesError AsErrorStringMapResponse1(ErrorStringMapResponse1 value) =>
        new(Optional<ErrorStringMapResponse1>.Some(value), default);

    private static CreateOrUpdateCouponCurrencyPricesError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetErrorStringMapResponse1(out ErrorStringMapResponse1 value) =>
        _errorStringMapResponse1Value.TryGetValue(out value);

    internal static Task<CreateOrUpdateCouponCurrencyPricesError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<ErrorStringMapResponse1>(response, ct).As(AsErrorStringMapResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreateOrUpdateCouponCurrencyPricesErrorResponse : IErrorResponse<CreateOrUpdateCouponCurrencyPricesError>
{
    public static CreateOrUpdateCouponCurrencyPricesErrorResponse Instance { get; } = new();

    private CreateOrUpdateCouponCurrencyPricesErrorResponse()
    {
    }

    public Task<CreateOrUpdateCouponCurrencyPricesError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreateOrUpdateCouponCurrencyPricesError.Create(response, ct);
}
