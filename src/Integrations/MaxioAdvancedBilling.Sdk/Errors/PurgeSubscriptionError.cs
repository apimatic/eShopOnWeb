using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class PurgeSubscriptionError : ApiError
{
    private readonly Optional<SubscriptionResponse> _subscriptionResponseValue;

    private PurgeSubscriptionError(Optional<SubscriptionResponse> subscriptionResponseValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _subscriptionResponseValue = subscriptionResponseValue;
    }

    private static PurgeSubscriptionError AsSubscriptionResponse(SubscriptionResponse value) =>
        new(Optional<SubscriptionResponse>.Some(value), default);

    private static PurgeSubscriptionError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSubscriptionResponse(out SubscriptionResponse value) =>
        _subscriptionResponseValue.TryGetValue(out value);

    internal static Task<PurgeSubscriptionError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 => FromJson<SubscriptionResponse>(response, ct).As(AsSubscriptionResponse),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class PurgeSubscriptionErrorResponse : IErrorResponse<PurgeSubscriptionError>
{
    public static PurgeSubscriptionErrorResponse Instance { get; } = new();

    private PurgeSubscriptionErrorResponse()
    {
    }

    public Task<PurgeSubscriptionError> Map(HttpResponseMessage response, CancellationToken ct) =>
        PurgeSubscriptionError.Create(response, ct);
}
