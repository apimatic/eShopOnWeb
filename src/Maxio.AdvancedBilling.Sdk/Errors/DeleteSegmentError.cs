using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class DeleteSegmentError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private DeleteSegmentError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static DeleteSegmentError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static DeleteSegmentError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<DeleteSegmentError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 or 422 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class DeleteSegmentErrorResponse : IErrorResponse<DeleteSegmentError>
{
    public static DeleteSegmentErrorResponse Instance { get; } = new();

    private DeleteSegmentErrorResponse()
    {
    }

    public Task<DeleteSegmentError> Map(HttpResponseMessage response, CancellationToken ct) =>
        DeleteSegmentError.Create(response, ct);
}
