using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class IssueAdvanceInvoiceError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private IssueAdvanceInvoiceError(Optional<RawError> noContentValue,
        Optional<ErrorListResponse1> errorListResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
        _errorListResponse1Value = errorListResponse1Value;
    }

    private static IssueAdvanceInvoiceError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default, default);

    private static IssueAdvanceInvoiceError AsErrorListResponse1(ErrorListResponse1 value) =>
        new(default, Optional<ErrorListResponse1>.Some(value), default);

    private static IssueAdvanceInvoiceError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    internal static Task<IssueAdvanceInvoiceError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            422 => FromJson<ErrorListResponse1>(response, ct).As(AsErrorListResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class IssueAdvanceInvoiceErrorResponse : IErrorResponse<IssueAdvanceInvoiceError>
{
    public static IssueAdvanceInvoiceErrorResponse Instance { get; } = new();

    private IssueAdvanceInvoiceErrorResponse()
    {
    }

    public Task<IssueAdvanceInvoiceError> Map(HttpResponseMessage response, CancellationToken ct) =>
        IssueAdvanceInvoiceError.Create(response, ct);
}
