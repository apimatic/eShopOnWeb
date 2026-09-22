using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class FindSubscriptionError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private FindSubscriptionError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static FindSubscriptionError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static FindSubscriptionError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<FindSubscriptionError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class FindSubscriptionErrorResponse : IErrorResponse<FindSubscriptionError>
{
    public static FindSubscriptionErrorResponse Instance { get; } = new();

    private FindSubscriptionErrorResponse()
    {
    }

    public Task<FindSubscriptionError> Map(HttpResponseMessage response, CancellationToken ct) =>
        FindSubscriptionError.Create(response, ct);
}
