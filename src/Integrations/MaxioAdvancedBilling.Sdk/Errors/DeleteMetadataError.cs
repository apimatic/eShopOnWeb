using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class DeleteMetadataError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private DeleteMetadataError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static DeleteMetadataError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static DeleteMetadataError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<DeleteMetadataError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class DeleteMetadataErrorResponse : IErrorResponse<DeleteMetadataError>
{
    public static DeleteMetadataErrorResponse Instance { get; } = new();

    private DeleteMetadataErrorResponse()
    {
    }

    public Task<DeleteMetadataError> Map(HttpResponseMessage response, CancellationToken ct) =>
        DeleteMetadataError.Create(response, ct);
}
