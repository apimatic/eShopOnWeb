using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class DeleteSubscriptionGroupError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private DeleteSubscriptionGroupError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static DeleteSubscriptionGroupError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static DeleteSubscriptionGroupError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<DeleteSubscriptionGroupError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class DeleteSubscriptionGroupErrorResponse : IErrorResponse<DeleteSubscriptionGroupError>
{
    public static DeleteSubscriptionGroupErrorResponse Instance { get; } = new();

    private DeleteSubscriptionGroupErrorResponse()
    {
    }

    public Task<DeleteSubscriptionGroupError> Map(HttpResponseMessage response, CancellationToken ct) =>
        DeleteSubscriptionGroupError.Create(response, ct);
}
