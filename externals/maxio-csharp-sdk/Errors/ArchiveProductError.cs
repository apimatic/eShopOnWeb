using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Maxio.Core.ErrorResponse;
using Maxio.Core.Models;
using Maxio.Models;

namespace Maxio.Errors;

public sealed class ArchiveProductError : ApiError
{
    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private ArchiveProductError(Optional<ErrorListResponse1> errorListResponse1Value, Optional<RawError> fallback) : base(fallback)
    {
        _errorListResponse1Value = errorListResponse1Value;
    }

    private static ArchiveProductError AsErrorListResponse1(ErrorListResponse1 value) =>
        new(Optional<ErrorListResponse1>.Some(value), default);

    private static ArchiveProductError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    internal static Task<ArchiveProductError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<ErrorListResponse1>(response, ct).As(AsErrorListResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ArchiveProductErrorResponse : IErrorResponse<ArchiveProductError>
{
    public static ArchiveProductErrorResponse Instance { get; } = new();

    private ArchiveProductErrorResponse()
    {
    }

    public Task<ArchiveProductError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ArchiveProductError.Create(response, ct);
}
