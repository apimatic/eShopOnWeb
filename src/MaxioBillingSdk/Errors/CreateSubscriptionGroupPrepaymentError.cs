using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class CreateSubscriptionGroupPrepaymentError : ApiError
{
    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private CreateSubscriptionGroupPrepaymentError(Optional<ErrorListResponse1> errorListResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _errorListResponse1Value = errorListResponse1Value;
    }

    private static CreateSubscriptionGroupPrepaymentError AsErrorListResponse1(ErrorListResponse1 value) =>
        new(Optional<ErrorListResponse1>.Some(value), default);

    private static CreateSubscriptionGroupPrepaymentError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    internal static Task<CreateSubscriptionGroupPrepaymentError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<ErrorListResponse1>(response, ct).As(AsErrorListResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreateSubscriptionGroupPrepaymentErrorResponse : IErrorResponse<CreateSubscriptionGroupPrepaymentError>
{
    public static CreateSubscriptionGroupPrepaymentErrorResponse Instance { get; } = new();

    private CreateSubscriptionGroupPrepaymentErrorResponse()
    {
    }

    public Task<CreateSubscriptionGroupPrepaymentError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreateSubscriptionGroupPrepaymentError.Create(response, ct);
}
