using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<SubscriptionPlansResponse> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionResponse> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken);
    Task<MySubscriptionsResponse> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private const string ReferencePrefix = "eshop";
    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionService(IMaxioAdvancedBillingClient maxio, UserManager<ApplicationUser> userManager)
    {
        _maxio = maxio;
        _userManager = userManager;
    }

    public async Task<SubscriptionPlansResponse> GetPlansAsync(CancellationToken cancellationToken)
    {
        var plans = await _maxio.GetPlansAsync(cancellationToken);
        return new SubscriptionPlansResponse(plans.Select(ToResponse).ToArray());
    }

    public async Task<SubscriptionResponse> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userName);
        var plans = await _maxio.GetPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(x => string.Equals(x.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new UnknownSubscriptionPlanException(planHandle);
        }

        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var subscriptionReference = $"{ReferencePrefix}-subscription-{user.Id}-{plan.Handle}";
        var existing = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return ToResponse(existing);
        }

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(new MaxioSubscriptionInput(
                subscriptionReference, plan.Handle, customer.Id, StableToken(subscriptionReference)), cancellationToken);
            return ToResponse(created);
        }
        catch (MaxioApiException exception) when (exception.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request may have won the race. The deterministic reference is the source of truth.
            var createdByConcurrentRequest = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (createdByConcurrentRequest is not null)
            {
                return ToResponse(createdByConcurrentRequest);
            }

            throw;
        }
    }

    public async Task<MySubscriptionsResponse> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userName);
        var customer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return new MySubscriptionsResponse(Array.Empty<SubscriptionResponse>());
        }

        var subscriptions = await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return new MySubscriptionsResponse(subscriptions
            .Where(x => x.Reference.StartsWith($"{ReferencePrefix}-subscription-{user.Id}-", StringComparison.Ordinal))
            .Select(ToResponse)
            .ToArray());
    }

    private async Task<ApplicationUser> GetUserAsync(string userName) =>
        await _userManager.FindByNameAsync(userName) ?? throw new UnknownAuthenticatedUserException(userName);

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName ?? throw new UnknownAuthenticatedUserException(user.Id);
        var firstName = email.Split('@', 2)[0];
        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCustomerInput(reference, firstName, "Shopper", email), cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.UnprocessableEntity)
        {
            var createdByConcurrentRequest = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (createdByConcurrentRequest is not null)
            {
                return createdByConcurrentRequest;
            }

            throw;
        }
    }

    private static string CustomerReference(string userId) => $"{ReferencePrefix}-user-{userId}";

    private static string StableToken(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static SubscriptionPlanResponse ToResponse(MaxioPlan plan) => new(plan.Handle, plan.Name, plan.Description, plan.PriceInCents, plan.Interval, plan.IntervalUnit, plan.Currency);

    private static SubscriptionResponse ToResponse(MaxioSubscription subscription) => new(subscription.Id, subscription.Plan.Handle,
        subscription.Plan.Name, subscription.Plan.PriceInCents, subscription.Plan.Currency, subscription.State, subscription.NextBillingAt);
}

public sealed class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string planHandle) : base($"The subscription plan '{planHandle}' is not available.") { }
}

public sealed class UnknownAuthenticatedUserException : Exception
{
    public UnknownAuthenticatedUserException(string userName) : base($"The authenticated user '{userName}' no longer exists.") { }
}
