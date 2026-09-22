using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class PatchSubscriptionError : ApiError
{
    private readonly Optional<SubscriptionError> _subscriptionErrorValue;

    private PatchSubscriptionError(Optional<SubscriptionError> subscriptionErrorValue, Optional<RawError> fallback) : base(fallback)
    {
        _subscriptionErrorValue = subscriptionErrorValue;
    }

    private static PatchSubscriptionError AsSubscriptionError(SubscriptionError value) =>
        new(Optional<SubscriptionError>.Some(value), default);

    private static PatchSubscriptionError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSubscriptionError(out SubscriptionError value) =>
        _subscriptionErrorValue.TryGetValue(out value);

    internal static Task<PatchSubscriptionError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 or 403 or 404 or 422 or 500 => FromJson<SubscriptionError>(response, ct).As(AsSubscriptionError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class PatchSubscriptionErrorResponse : IErrorResponse<PatchSubscriptionError>
{
    public static PatchSubscriptionErrorResponse Instance { get; } = new();

    private PatchSubscriptionErrorResponse()
    {
    }

    public Task<PatchSubscriptionError> Map(HttpResponseMessage response, CancellationToken ct) =>
        PatchSubscriptionError.Create(response, ct);
}
