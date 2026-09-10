using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ReadSubscriptionComponentError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ReadSubscriptionComponentError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ReadSubscriptionComponentError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ReadSubscriptionComponentError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ReadSubscriptionComponentError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ReadSubscriptionComponentErrorResponse : IErrorResponse<ReadSubscriptionComponentError>
{
    public static ReadSubscriptionComponentErrorResponse Instance { get; } = new();

    private ReadSubscriptionComponentErrorResponse()
    {
    }

    public Task<ReadSubscriptionComponentError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ReadSubscriptionComponentError.Create(response, ct);
}
