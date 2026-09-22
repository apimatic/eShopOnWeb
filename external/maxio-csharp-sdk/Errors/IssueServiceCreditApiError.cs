using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models.AnyOf;

namespace MaxioAdvancedBilling.Errors;

public sealed class IssueServiceCreditApiError : ApiError
{
    private readonly Optional<IssueServiceCreditErrorResponse> _issueServiceCreditErrorResponseValue;

    private IssueServiceCreditApiError(Optional<IssueServiceCreditErrorResponse> issueServiceCreditErrorResponseValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _issueServiceCreditErrorResponseValue = issueServiceCreditErrorResponseValue;
    }

    private static IssueServiceCreditApiError AsIssueServiceCreditErrorResponse(IssueServiceCreditErrorResponse value) =>
        new(Optional<IssueServiceCreditErrorResponse>.Some(value), default);

    private static IssueServiceCreditApiError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetIssueServiceCreditErrorResponse(out IssueServiceCreditErrorResponse value) =>
        _issueServiceCreditErrorResponseValue.TryGetValue(out value);

    internal static Task<IssueServiceCreditApiError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<IssueServiceCreditErrorResponse>(response, ct).As(AsIssueServiceCreditErrorResponse),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class IssueServiceCreditApiErrorResponse : IErrorResponse<IssueServiceCreditApiError>
{
    public static IssueServiceCreditApiErrorResponse Instance { get; } = new();

    private IssueServiceCreditApiErrorResponse()
    {
    }

    public Task<IssueServiceCreditApiError> Map(HttpResponseMessage response, CancellationToken ct) =>
        IssueServiceCreditApiError.Create(response, ct);
}
