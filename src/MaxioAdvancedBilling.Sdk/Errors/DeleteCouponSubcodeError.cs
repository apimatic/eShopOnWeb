using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class DeleteCouponSubcodeError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private DeleteCouponSubcodeError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static DeleteCouponSubcodeError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static DeleteCouponSubcodeError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<DeleteCouponSubcodeError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class DeleteCouponSubcodeErrorResponse : IErrorResponse<DeleteCouponSubcodeError>
{
    public static DeleteCouponSubcodeErrorResponse Instance { get; } = new();

    private DeleteCouponSubcodeErrorResponse()
    {
    }

    public Task<DeleteCouponSubcodeError> Map(HttpResponseMessage response, CancellationToken ct) =>
        DeleteCouponSubcodeError.Create(response, ct);
}
