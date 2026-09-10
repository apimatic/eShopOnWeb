using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class UpdateSubscriptionGroupMembersError : ApiError
{
    private readonly Optional<SubscriptionGroupUpdateErrorResponse1> _subscriptionGroupUpdateErrorResponse1Value;

    private UpdateSubscriptionGroupMembersError(Optional<SubscriptionGroupUpdateErrorResponse1> subscriptionGroupUpdateErrorResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _subscriptionGroupUpdateErrorResponse1Value = subscriptionGroupUpdateErrorResponse1Value;
    }

    private static UpdateSubscriptionGroupMembersError AsSubscriptionGroupUpdateErrorResponse1(SubscriptionGroupUpdateErrorResponse1 value) =>
        new(Optional<SubscriptionGroupUpdateErrorResponse1>.Some(value), default);

    private static UpdateSubscriptionGroupMembersError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSubscriptionGroupUpdateErrorResponse1(out SubscriptionGroupUpdateErrorResponse1 value) =>
        _subscriptionGroupUpdateErrorResponse1Value.TryGetValue(out value);

    internal static Task<UpdateSubscriptionGroupMembersError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<SubscriptionGroupUpdateErrorResponse1>(response, ct).As(AsSubscriptionGroupUpdateErrorResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class UpdateSubscriptionGroupMembersErrorResponse : IErrorResponse<UpdateSubscriptionGroupMembersError>
{
    public static UpdateSubscriptionGroupMembersErrorResponse Instance { get; } = new();

    private UpdateSubscriptionGroupMembersErrorResponse()
    {
    }

    public Task<UpdateSubscriptionGroupMembersError> Map(HttpResponseMessage response, CancellationToken ct) =>
        UpdateSubscriptionGroupMembersError.Create(response, ct);
}
