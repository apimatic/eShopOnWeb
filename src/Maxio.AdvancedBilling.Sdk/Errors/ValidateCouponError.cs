using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ValidateCouponError : ApiError
{
    private readonly Optional<SingleStringErrorResponse1> _singleStringErrorResponse1Value;

    private ValidateCouponError(Optional<SingleStringErrorResponse1> singleStringErrorResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _singleStringErrorResponse1Value = singleStringErrorResponse1Value;
    }

    private static ValidateCouponError AsSingleStringErrorResponse1(SingleStringErrorResponse1 value) =>
        new(Optional<SingleStringErrorResponse1>.Some(value), default);

    private static ValidateCouponError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSingleStringErrorResponse1(out SingleStringErrorResponse1 value) =>
        _singleStringErrorResponse1Value.TryGetValue(out value);

    internal static Task<ValidateCouponError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromJson<SingleStringErrorResponse1>(response, ct).As(AsSingleStringErrorResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ValidateCouponErrorResponse : IErrorResponse<ValidateCouponError>
{
    public static ValidateCouponErrorResponse Instance { get; } = new();

    private ValidateCouponErrorResponse()
    {
    }

    public Task<ValidateCouponError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ValidateCouponError.Create(response, ct);
}
