using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Maxio.Core.ErrorResponse;
using Maxio.Core.Models;
using Maxio.Models;

namespace Maxio.Errors;

public sealed class DeleteInvoiceError : ApiError
{
    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private DeleteInvoiceError(Optional<ErrorListResponse1> errorListResponse1Value, Optional<RawError> fallback) : base(fallback)
    {
        _errorListResponse1Value = errorListResponse1Value;
    }

    private static DeleteInvoiceError AsErrorListResponse1(ErrorListResponse1 value) =>
        new(Optional<ErrorListResponse1>.Some(value), default);

    private static DeleteInvoiceError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    internal static Task<DeleteInvoiceError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 or 422 => FromJson<ErrorListResponse1>(response, ct).As(AsErrorListResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class DeleteInvoiceErrorResponse : IErrorResponse<DeleteInvoiceError>
{
    public static DeleteInvoiceErrorResponse Instance { get; } = new();

    private DeleteInvoiceErrorResponse()
    {
    }

    public Task<DeleteInvoiceError> Map(HttpResponseMessage response, CancellationToken ct) =>
        DeleteInvoiceError.Create(response, ct);
}
