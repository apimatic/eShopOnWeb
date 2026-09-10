using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ListMrrPerSubscriptionError : ApiError
{
    private readonly Optional<SubscriptionsMrrErrorResponse1> _subscriptionsMrrErrorResponse1Value;

    private ListMrrPerSubscriptionError(Optional<SubscriptionsMrrErrorResponse1> subscriptionsMrrErrorResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _subscriptionsMrrErrorResponse1Value = subscriptionsMrrErrorResponse1Value;
    }

    private static ListMrrPerSubscriptionError AsSubscriptionsMrrErrorResponse1(SubscriptionsMrrErrorResponse1 value) =>
        new(Optional<SubscriptionsMrrErrorResponse1>.Some(value), default);

    private static ListMrrPerSubscriptionError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSubscriptionsMrrErrorResponse1(out SubscriptionsMrrErrorResponse1 value) =>
        _subscriptionsMrrErrorResponse1Value.TryGetValue(out value);

    internal static Task<ListMrrPerSubscriptionError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 => FromJson<SubscriptionsMrrErrorResponse1>(response, ct).As(AsSubscriptionsMrrErrorResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ListMrrPerSubscriptionErrorResponse : IErrorResponse<ListMrrPerSubscriptionError>
{
    public static ListMrrPerSubscriptionErrorResponse Instance { get; } = new();

    private ListMrrPerSubscriptionErrorResponse()
    {
    }

    public Task<ListMrrPerSubscriptionError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ListMrrPerSubscriptionError.Create(response, ct);
}
