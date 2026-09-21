using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class CreateSubscriptionGroupError : ApiError
{
    private readonly Optional<SubscriptionGroupCreateErrorResponse1> _subscriptionGroupCreateErrorResponse1Value;

    private CreateSubscriptionGroupError(Optional<SubscriptionGroupCreateErrorResponse1> subscriptionGroupCreateErrorResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _subscriptionGroupCreateErrorResponse1Value = subscriptionGroupCreateErrorResponse1Value;
    }

    private static CreateSubscriptionGroupError AsSubscriptionGroupCreateErrorResponse1(SubscriptionGroupCreateErrorResponse1 value) =>
        new(Optional<SubscriptionGroupCreateErrorResponse1>.Some(value), default);

    private static CreateSubscriptionGroupError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSubscriptionGroupCreateErrorResponse1(out SubscriptionGroupCreateErrorResponse1 value) =>
        _subscriptionGroupCreateErrorResponse1Value.TryGetValue(out value);

    internal static Task<CreateSubscriptionGroupError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<SubscriptionGroupCreateErrorResponse1>(response, ct).As(AsSubscriptionGroupCreateErrorResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreateSubscriptionGroupErrorResponse : IErrorResponse<CreateSubscriptionGroupError>
{
    public static CreateSubscriptionGroupErrorResponse Instance { get; } = new();

    private CreateSubscriptionGroupErrorResponse()
    {
    }

    public Task<CreateSubscriptionGroupError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreateSubscriptionGroupError.Create(response, ct);
}
