using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ValidateReferralCodeError : ApiError
{
    private readonly Optional<SingleStringErrorResponse1> _singleStringErrorResponse1Value;

    private ValidateReferralCodeError(Optional<SingleStringErrorResponse1> singleStringErrorResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _singleStringErrorResponse1Value = singleStringErrorResponse1Value;
    }

    private static ValidateReferralCodeError AsSingleStringErrorResponse1(SingleStringErrorResponse1 value) =>
        new(Optional<SingleStringErrorResponse1>.Some(value), default);

    private static ValidateReferralCodeError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSingleStringErrorResponse1(out SingleStringErrorResponse1 value) =>
        _singleStringErrorResponse1Value.TryGetValue(out value);

    internal static Task<ValidateReferralCodeError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromJson<SingleStringErrorResponse1>(response, ct).As(AsSingleStringErrorResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ValidateReferralCodeErrorResponse : IErrorResponse<ValidateReferralCodeError>
{
    public static ValidateReferralCodeErrorResponse Instance { get; } = new();

    private ValidateReferralCodeErrorResponse()
    {
    }

    public Task<ValidateReferralCodeError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ValidateReferralCodeError.Create(response, ct);
}
