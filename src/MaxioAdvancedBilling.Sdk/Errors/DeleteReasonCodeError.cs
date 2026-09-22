using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class DeleteReasonCodeError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private DeleteReasonCodeError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static DeleteReasonCodeError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static DeleteReasonCodeError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<DeleteReasonCodeError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class DeleteReasonCodeErrorResponse : IErrorResponse<DeleteReasonCodeError>
{
    public static DeleteReasonCodeErrorResponse Instance { get; } = new();

    private DeleteReasonCodeErrorResponse()
    {
    }

    public Task<DeleteReasonCodeError> Map(HttpResponseMessage response, CancellationToken ct) =>
        DeleteReasonCodeError.Create(response, ct);
}
