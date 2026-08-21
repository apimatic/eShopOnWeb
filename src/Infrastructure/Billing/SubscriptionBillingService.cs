using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CompetingClaimWait = TimeSpan.FromSeconds(5);
    private readonly IMaxioBillingGateway _gateway;
    private readonly ISubscriptionLinkStore _store;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AsyncKeyedLocker _keyedLocker;
    private readonly TimeProvider _timeProvider;

    public SubscriptionBillingService(
        IMaxioBillingGateway gateway,
        ISubscriptionLinkStore store,
        UserManager<ApplicationUser> userManager,
        AsyncKeyedLocker keyedLocker,
        TimeProvider timeProvider)
    {
        _gateway = gateway;
        _store = store;
        _userManager = userManager;
        _keyedLocker = keyedLocker;
        _timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
        _gateway.ListPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(
        string userName,
        string productHandle,
        string? pricePointHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw RequestError("product_handle_required", "A productHandle is required.");
        }

        var user = await ResolveUserAsync(userName);
        var plans = await _gateway.ListPlansAsync(cancellationToken);
        var requestedPlan = ResolvePlan(plans, productHandle, pricePointHandle);
        var canonicalPricePoint = requestedPlan.PricePointHandle ?? string.Empty;
        var operationKey = string.Join('\u001f', user.Id, requestedPlan.ProductHandle, canonicalPricePoint);

        using (await _keyedLocker.LockAsync(operationKey, cancellationToken))
        {
            var leaseId = Guid.NewGuid();
            var now = _timeProvider.GetUtcNow();
            var subscriptionReference = BuildReference("eshop-sub", operationKey, 40);
            var claim = await _store.ClaimSubscriptionAsync(
                user.Id,
                requestedPlan.ProductHandle,
                canonicalPricePoint,
                subscriptionReference,
                leaseId,
                now,
                cancellationToken);

            if (!claim.Acquired)
            {
                if (claim.Link.IsConfirmed)
                {
                    var confirmed = await ReadAndValidateAsync(
                        claim.Link,
                        expectedCustomerReference: BuildReference("eshop-user", user.Id, 32),
                        cancellationToken);
                    return new SubscribeResult(Created: false, confirmed);
                }

                var winner = await WaitForCompetingClaimAsync(
                    user.Id,
                    requestedPlan.ProductHandle,
                    canonicalPricePoint,
                    cancellationToken);
                if (winner?.IsConfirmed == true)
                {
                    var confirmed = await ReadAndValidateAsync(
                        winner,
                        expectedCustomerReference: BuildReference("eshop-user", user.Id, 32),
                        cancellationToken);
                    return new SubscribeResult(Created: false, confirmed);
                }

                throw new SubscriptionBillingException(
                    HttpStatusCode.Conflict,
                    "subscription_in_progress",
                    "An identical subscription request is already in progress. Please retry shortly.");
            }

            return await CompleteClaimAsync(
                user,
                requestedPlan,
                pricePointHandle,
                claim.Link,
                leaseId,
                cancellationToken);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListForUserAsync(
        string userName,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(userName);
        var customerReference = BuildReference("eshop-user", user.Id, 32);
        var customer = await _gateway.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        return await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<SubscribeResult> CompleteClaimAsync(
        ApplicationUser user,
        SubscriptionPlan plan,
        string? requestedPricePointHandle,
        MaxioSubscriptionLink link,
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var reconciled = await _gateway.FindSubscriptionAsync(
            link.SubscriptionReference,
            cancellationToken);
        if (reconciled is not null)
        {
            ValidateSubscription(reconciled, customer.Reference, plan, link.SubscriptionReference);
            await _store.ConfirmSubscriptionAsync(
                link,
                leaseId,
                reconciled.Id,
                _timeProvider.GetUtcNow(),
                cancellationToken);
            return new SubscribeResult(Created: false, reconciled);
        }

        try
        {
            var created = await _gateway.CreateSubscriptionAsync(
                new CreateBillingSubscription(
                    plan.ProductHandle,
                    requestedPricePointHandle,
                    customer.Reference,
                    link.SubscriptionReference),
                cancellationToken);
            ValidateSubscription(created, customer.Reference, plan, link.SubscriptionReference);
            await _store.ConfirmSubscriptionAsync(
                link,
                leaseId,
                created.Id,
                _timeProvider.GetUtcNow(),
                cancellationToken);
            return new SubscribeResult(Created: true, created);
        }
        catch (BillingProviderException exception)
        {
            var afterFailure = await TryReconcileAsync(
                link.SubscriptionReference,
                customer.Reference,
                plan,
                cancellationToken);
            if (afterFailure is not null)
            {
                await _store.ConfirmSubscriptionAsync(
                    link,
                    leaseId,
                    afterFailure.Id,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
                return new SubscribeResult(Created: false, afterFailure);
            }

            if (!exception.OutcomeUnknown)
            {
                await _store.FailSubscriptionAsync(
                    link,
                    leaseId,
                    exception.Code,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
            }

            throw;
        }
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var reference = BuildReference("eshop-user", user.Id, 32);
        var existing = await _gateway.FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            await SaveCustomerLinkAsync(user.Id, existing, cancellationToken);
            return existing;
        }

        if (string.IsNullOrWhiteSpace(user.Email) ||
            string.IsNullOrWhiteSpace(user.FirstName) ||
            string.IsNullOrWhiteSpace(user.LastName))
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.UnprocessableEntity,
                "billing_profile_incomplete",
                "Your account needs a first name, last name, and email before you can subscribe.");
        }

        try
        {
            var created = await _gateway.CreateCustomerAsync(
                new CreateBillingCustomer(reference, user.FirstName, user.LastName, user.Email),
                cancellationToken);
            await SaveCustomerLinkAsync(user.Id, created, cancellationToken);
            return created;
        }
        catch (BillingProviderException exception) when (
            exception.StatusCode == HttpStatusCode.UnprocessableEntity || exception.OutcomeUnknown)
        {
            var raced = await _gateway.FindCustomerAsync(reference, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            await SaveCustomerLinkAsync(user.Id, raced, cancellationToken);
            return raced;
        }
    }

    private async Task SaveCustomerLinkAsync(
        string userId,
        BillingCustomer customer,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var link = new MaxioCustomerLink(userId, customer.Reference, customer.Id, now);
        await _store.SaveCustomerAsync(link, cancellationToken);
    }

    private async Task<SubscriptionDetails?> TryReconcileAsync(
        string subscriptionReference,
        string customerReference,
        SubscriptionPlan plan,
        CancellationToken cancellationToken)
    {
        try
        {
            var subscription = await _gateway.FindSubscriptionAsync(
                subscriptionReference,
                cancellationToken);
            if (subscription is not null)
            {
                ValidateSubscription(subscription, customerReference, plan, subscriptionReference);
            }

            return subscription;
        }
        catch (BillingProviderException)
        {
            return null;
        }
    }

    private async Task<SubscriptionDetails> ReadAndValidateAsync(
        MaxioSubscriptionLink link,
        string expectedCustomerReference,
        CancellationToken cancellationToken)
    {
        var subscription = await _gateway.ReadSubscriptionAsync(
            link.MaxioSubscriptionId!.Value,
            cancellationToken);
        var plan = new SubscriptionPlan(
            link.ProductHandle,
            string.IsNullOrEmpty(link.PricePointHandle) ? null : link.PricePointHandle,
            subscription.ProductName ?? link.ProductHandle,
            subscription.PriceInCents ?? 0,
            Interval: null,
            IntervalUnit: null);
        ValidateSubscription(subscription, expectedCustomerReference, plan, link.SubscriptionReference);
        return subscription;
    }

    private async Task<MaxioSubscriptionLink?> WaitForCompetingClaimAsync(
        string userId,
        string productHandle,
        string pricePointHandle,
        CancellationToken cancellationToken)
    {
        var deadline = _timeProvider.GetUtcNow().Add(CompetingClaimWait);
        while (_timeProvider.GetUtcNow() < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            var link = await _store.FindSubscriptionAsync(
                userId,
                productHandle,
                pricePointHandle,
                cancellationToken);
            if (link is null || link.Status != MaxioSubscriptionLink.PendingStatus)
            {
                return link;
            }
        }

        return await _store.FindSubscriptionAsync(
            userId,
            productHandle,
            pricePointHandle,
            cancellationToken);
    }

    private async Task<ApplicationUser> ResolveUserAsync(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.Unauthorized,
                "authenticated_user_required",
                "A valid bearer token is required.");
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.Unauthorized,
                "authenticated_user_not_found",
                "The authenticated account could not be resolved.");
        }

        return user;
    }

    private static SubscriptionPlan ResolvePlan(
        IReadOnlyList<SubscriptionPlan> plans,
        string productHandle,
        string? pricePointHandle)
    {
        var productMatches = plans
            .Where(plan => string.Equals(plan.ProductHandle, productHandle, StringComparison.Ordinal))
            .ToList();
        if (productMatches.Count == 0)
        {
            throw RequestError("subscription_plan_not_found", "The requested subscription plan was not found.");
        }

        if (pricePointHandle is null)
        {
            if (productMatches.Count != 1)
            {
                throw RequestError(
                    "price_point_required",
                    "A pricePointHandle is required for this subscription plan.");
            }

            return productMatches[0];
        }

        var match = productMatches.SingleOrDefault(plan =>
            string.Equals(plan.PricePointHandle, pricePointHandle, StringComparison.Ordinal));
        return match ?? throw RequestError(
            "subscription_price_point_not_found",
            "The requested price point was not found for this subscription plan.");
    }

    private static void ValidateSubscription(
        SubscriptionDetails subscription,
        string customerReference,
        SubscriptionPlan plan,
        string subscriptionReference)
    {
        var expectedPricePoint = plan.PricePointHandle;
        if (!string.Equals(subscription.CustomerReference, customerReference, StringComparison.Ordinal) ||
            !string.Equals(subscription.ProductHandle, plan.ProductHandle, StringComparison.Ordinal) ||
            expectedPricePoint is not null &&
                !string.Equals(subscription.PricePointHandle, expectedPricePoint, StringComparison.Ordinal) ||
            !string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal))
        {
            throw new BillingProviderException(
                HttpStatusCode.BadGateway,
                "provider_identity_mismatch",
                "Maxio returned a subscription that did not match the requested account and plan.",
                outcomeUnknown: false);
        }
    }

    private static string BuildReference(string prefix, string value, int hexLength)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"{prefix}-{Convert.ToHexString(hash).ToLowerInvariant()[..hexLength]}";
    }

    private static SubscriptionBillingException RequestError(string code, string message) =>
        new(HttpStatusCode.BadRequest, code, message);
}
