using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class SignupWithSubscriptionGroupError : ApiError
{
    private readonly Optional<SubscriptionGroupSignupErrorResponse1> _subscriptionGroupSignupErrorResponse1Value;

    private SignupWithSubscriptionGroupError(Optional<SubscriptionGroupSignupErrorResponse1> subscriptionGroupSignupErrorResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _subscriptionGroupSignupErrorResponse1Value = subscriptionGroupSignupErrorResponse1Value;
    }

    private static SignupWithSubscriptionGroupError AsSubscriptionGroupSignupErrorResponse1(SubscriptionGroupSignupErrorResponse1 value) =>
        new(Optional<SubscriptionGroupSignupErrorResponse1>.Some(value), default);

    private static SignupWithSubscriptionGroupError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSubscriptionGroupSignupErrorResponse1(out SubscriptionGroupSignupErrorResponse1 value) =>
        _subscriptionGroupSignupErrorResponse1Value.TryGetValue(out value);

    internal static Task<SignupWithSubscriptionGroupError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<SubscriptionGroupSignupErrorResponse1>(response, ct).As(AsSubscriptionGroupSignupErrorResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class SignupWithSubscriptionGroupErrorResponse : IErrorResponse<SignupWithSubscriptionGroupError>
{
    public static SignupWithSubscriptionGroupErrorResponse Instance { get; } = new();

    private SignupWithSubscriptionGroupErrorResponse()
    {
    }

    public Task<SignupWithSubscriptionGroupError> Map(HttpResponseMessage response, CancellationToken ct) =>
        SignupWithSubscriptionGroupError.Create(response, ct);
}
