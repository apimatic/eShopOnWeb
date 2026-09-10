using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ResumeSubscriptionError : ApiError
{
    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private ResumeSubscriptionError(Optional<ErrorListResponse1> errorListResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _errorListResponse1Value = errorListResponse1Value;
    }

    private static ResumeSubscriptionError AsErrorListResponse1(ErrorListResponse1 value) =>
        new(Optional<ErrorListResponse1>.Some(value), default);

    private static ResumeSubscriptionError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    internal static Task<ResumeSubscriptionError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<ErrorListResponse1>(response, ct).As(AsErrorListResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ResumeSubscriptionErrorResponse : IErrorResponse<ResumeSubscriptionError>
{
    public static ResumeSubscriptionErrorResponse Instance { get; } = new();

    private ResumeSubscriptionErrorResponse()
    {
    }

    public Task<ResumeSubscriptionError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ResumeSubscriptionError.Create(response, ct);
}
