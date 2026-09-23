using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models.AnyOf;

namespace MaxioAdvancedBilling.Errors;

public sealed class CreatePrepaymentApiError : ApiError
{
    private readonly Optional<CreatePrepaymentErrorResponse> _createPrepaymentErrorResponseValue;

    private CreatePrepaymentApiError(Optional<CreatePrepaymentErrorResponse> createPrepaymentErrorResponseValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _createPrepaymentErrorResponseValue = createPrepaymentErrorResponseValue;
    }

    private static CreatePrepaymentApiError AsCreatePrepaymentErrorResponse(CreatePrepaymentErrorResponse value) =>
        new(Optional<CreatePrepaymentErrorResponse>.Some(value), default);

    private static CreatePrepaymentApiError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetCreatePrepaymentErrorResponse(out CreatePrepaymentErrorResponse value) =>
        _createPrepaymentErrorResponseValue.TryGetValue(out value);

    internal static Task<CreatePrepaymentApiError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<CreatePrepaymentErrorResponse>(response, ct).As(AsCreatePrepaymentErrorResponse),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreatePrepaymentApiErrorResponse : IErrorResponse<CreatePrepaymentApiError>
{
    public static CreatePrepaymentApiErrorResponse Instance { get; } = new();

    private CreatePrepaymentApiErrorResponse()
    {
    }

    public Task<CreatePrepaymentApiError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreatePrepaymentApiError.Create(response, ct);
}
