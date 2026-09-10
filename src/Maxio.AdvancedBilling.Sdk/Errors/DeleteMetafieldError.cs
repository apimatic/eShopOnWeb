using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class DeleteMetafieldError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private DeleteMetafieldError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static DeleteMetafieldError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static DeleteMetafieldError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<DeleteMetafieldError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class DeleteMetafieldErrorResponse : IErrorResponse<DeleteMetafieldError>
{
    public static DeleteMetafieldErrorResponse Instance { get; } = new();

    private DeleteMetafieldErrorResponse()
    {
    }

    public Task<DeleteMetafieldError> Map(HttpResponseMessage response, CancellationToken ct) =>
        DeleteMetafieldError.Create(response, ct);
}
