using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Maxio.Core.ErrorResponse;
using Maxio.Core.Models;
using Maxio.Models;

namespace Maxio.Errors;

public sealed class RecordPaymentForInvoiceError : ApiError
{
    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private RecordPaymentForInvoiceError(Optional<ErrorListResponse1> errorListResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _errorListResponse1Value = errorListResponse1Value;
    }

    private static RecordPaymentForInvoiceError AsErrorListResponse1(ErrorListResponse1 value) =>
        new(Optional<ErrorListResponse1>.Some(value), default);

    private static RecordPaymentForInvoiceError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    internal static Task<RecordPaymentForInvoiceError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<ErrorListResponse1>(response, ct).As(AsErrorListResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class RecordPaymentForInvoiceErrorResponse : IErrorResponse<RecordPaymentForInvoiceError>
{
    public static RecordPaymentForInvoiceErrorResponse Instance { get; } = new();

    private RecordPaymentForInvoiceErrorResponse()
    {
    }

    public Task<RecordPaymentForInvoiceError> Map(HttpResponseMessage response, CancellationToken ct) =>
        RecordPaymentForInvoiceError.Create(response, ct);
}
