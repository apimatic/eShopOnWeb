using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioBillingClient _maxio;
    private readonly UserSubscriptionCoordinator _coordinator;

    public SubscriptionService(IMaxioBillingClient maxio, UserSubscriptionCoordinator coordinator)
    {
        _maxio = maxio;
        _coordinator = coordinator;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .OrderBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToPlan)
            .ToList();
    }

    public Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var normalizedHandle = planHandle?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedHandle))
        {
            throw new SubscriptionValidationException("planHandle is required.");
        }

        return _coordinator.ExecuteAsync(user.Id, async () =>
        {
            var products = await _maxio.ListProductsAsync(cancellationToken);
            var plan = products.SingleOrDefault(product =>
                product.ArchivedAt is null &&
                string.Equals(product.Handle, normalizedHandle, StringComparison.Ordinal));
            if (plan is null)
            {
                throw new SubscriptionValidationException("The requested plan is not available in the configured product family.");
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var subscriptionReference = SubscriptionReference(user.Id, normalizedHandle);
            var existing = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existingSubscription = existing.FirstOrDefault(subscription =>
                string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal));
            if (existingSubscription is not null)
            {
                return ToSubscription(existingSubscription);
            }

            var created = await _maxio.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = normalizedHandle,
                    CustomerId = customer.Id,
                    Reference = subscriptionReference
                }
            }, cancellationToken);

            return ToSubscription(created);
        });
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var customer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(subscription => subscription.Reference?.StartsWith(SubscriptionReferencePrefix(user.Id), StringComparison.Ordinal) == true)
            .OrderByDescending(subscription => subscription.NextAssessmentAt)
            .Select(ToSubscription)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new SubscriptionValidationException("The signed-in user does not have an email address.");
        }

        try
        {
            return await _maxio.CreateCustomerAsync(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    // ApplicationUser has no profile-name fields. These required Maxio
                    // fields are stable, non-empty display values; the email remains the
                    // eShop identity email.
                    FirstName = "eShop",
                    LastName = CustomerLastName(email),
                    Email = email,
                    Reference = reference
                }
            }, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio guarantees a unique customer reference. A concurrent create may
            // have won after our lookup; resolve that authoritative record instead.
            var concurrentlyCreatedCustomer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (concurrentlyCreatedCustomer is not null)
            {
                return concurrentlyCreatedCustomer;
            }

            throw;
        }
    }

    private static string CustomerReference(string userId) => $"eshop:user:{userId}";

    private static string SubscriptionReferencePrefix(string userId) => $"eshop:subscription:{userId}:";

    private static string SubscriptionReference(string userId, string planHandle) =>
        $"{SubscriptionReferencePrefix(userId)}{planHandle}";

    private static string CustomerLastName(string email)
    {
        var localPart = email.Split('@', 2)[0].Trim();
        return string.IsNullOrWhiteSpace(localPart) ? "Shopper" : localPart;
    }

    private static SubscriptionPlanDto ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static SubscriptionDto ToSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product.Handle ?? string.Empty,
        PlanName = subscription.Product.Name,
        PriceInCents = subscription.ProductPriceInCents,
        State = subscription.State,
        // next_assessment_at is the next date Maxio will assess/attempt recurring payment.
        NextBillingAt = subscription.NextAssessmentAt
    };
}

public sealed class SubscriptionValidationException : Exception
{
    public SubscriptionValidationException(string message) : base(message)
    {
    }
}
