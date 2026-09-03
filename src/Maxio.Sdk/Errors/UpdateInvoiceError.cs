using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Maxio.Core.ErrorResponse;
using Maxio.Core.Models;
using Maxio.Models;

namespace Maxio.Errors;

public sealed class UpdateInvoiceError : ApiError
{
    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private readonly Optional<ErrorArrayMapResponse1> _errorArrayMapResponse1Value;

    private UpdateInvoiceError(Optional<ErrorListResponse1> errorListResponse1Value,
        Optional<ErrorArrayMapResponse1> errorArrayMapResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _errorListResponse1Value = errorListResponse1Value;
        _errorArrayMapResponse1Value = errorArrayMapResponse1Value;
    }

    private static UpdateInvoiceError AsErrorListResponse1(ErrorListResponse1 value) =>
        new(Optional<ErrorListResponse1>.Some(value), default, default);

    private static UpdateInvoiceError AsErrorArrayMapResponse1(ErrorArrayMapResponse1 value) =>
        new(default, Optional<ErrorArrayMapResponse1>.Some(value), default);

    private static UpdateInvoiceError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    public bool TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1 value) =>
        _errorArrayMapResponse1Value.TryGetValue(out value);

    internal static Task<UpdateInvoiceError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromJson<ErrorListResponse1>(response, ct).As(AsErrorListResponse1),
            422 => FromJson<ErrorArrayMapResponse1>(response, ct).As(AsErrorArrayMapResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class UpdateInvoiceErrorResponse : IErrorResponse<UpdateInvoiceError>
{
    public static UpdateInvoiceErrorResponse Instance { get; } = new();

    private UpdateInvoiceErrorResponse()
    {
    }

    public Task<UpdateInvoiceError> Map(HttpResponseMessage response, CancellationToken ct) =>
        UpdateInvoiceError.Create(response, ct);
}
