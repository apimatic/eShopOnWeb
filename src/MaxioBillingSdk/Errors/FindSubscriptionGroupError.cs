using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class FindSubscriptionGroupError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private FindSubscriptionGroupError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static FindSubscriptionGroupError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static FindSubscriptionGroupError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<FindSubscriptionGroupError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class FindSubscriptionGroupErrorResponse : IErrorResponse<FindSubscriptionGroupError>
{
    public static FindSubscriptionGroupErrorResponse Instance { get; } = new();

    private FindSubscriptionGroupErrorResponse()
    {
    }

    public Task<FindSubscriptionGroupError> Map(HttpResponseMessage response, CancellationToken ct) =>
        FindSubscriptionGroupError.Create(response, ct);
}
