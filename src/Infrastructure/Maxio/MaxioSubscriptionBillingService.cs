using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscription;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing implementation backed by Maxio Advanced Billing as the billing
/// system of record.
///
/// Identity mapping (no local persistence needed, so it survives storage restarts):
///   - the eShopOnWeb user id maps to a Maxio customer via the customer's "reference"
///     field ("eshop-user:{userId}"), resolved with the spec's lookup-by-reference call;
///   - a (user, plan) pair maps to a Maxio subscription via the subscription's
///     "reference" field ("eshop-sub:{userId}:{planHandle}").
/// Both ensures are idempotent: concurrent double-clicks are serialized per user and a
/// lost create race is resolved by re-reading instead of creating a duplicate.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    public const string CustomerReferencePrefix = "eshop-user:";
    public const string SubscriptionReferencePrefix = "eshop-sub:";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly MaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioApiClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _client.ListProductsAsync(cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .Where(p => string.Equals(
                p.ProductFamily?.Handle,
                _options.ProductFamilyHandle,
                StringComparison.OrdinalIgnoreCase))
            .Select(p => new SubscriptionPlan
            {
                Id = p.Id,
                Handle = p.Handle ?? string.Empty,
                Name = p.Name,
                Description = p.Description,
                Price = p.PriceInCents / 100m,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty,
                RequiresPaymentMethod = p.RequireCreditCard,
                ProductFamilyHandle = p.ProductFamily?.Handle ?? string.Empty
            })
            .OrderBy(p => p.Price)
            .ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = ResolvePlan(plans, command)
            ?? throw new InvalidOperationException(
                $"Subscription plan not found in product family '{_options.ProductFamilyHandle}'.");

        var gate = UserLocks.GetOrAdd(command.Subscriber.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(command.Subscriber, cancellationToken);

            var subscriptionReference = SubscriptionReference(command.Subscriber.UserId, plan.Handle);
            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = subscriptions.FirstOrDefault(s =>
                string.Equals(s.Reference, subscriptionReference, StringComparison.OrdinalIgnoreCase) &&
                !IsTerminal(s.State));
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Idempotent subscribe: user {UserId} already holds subscription {SubscriptionId} for plan {PlanHandle}.",
                    command.Subscriber.UserId, existing.Id, plan.Handle);
                return Map(existing);
            }

            var created = await _client.CreateSubscriptionAsync(
                new Models.MaxioCreateSubscriptionRequest
                {
                    Subscription = new Models.MaxioCreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customer.Id,
                        Reference = subscriptionReference,
                        PaymentCollectionMethod = plan.RequiresPaymentMethod ? null : "remittance"
                    }
                },
                cancellationToken);

            _logger.LogInformation(
                "User {UserId} subscribed to plan {PlanHandle}: Maxio subscription {SubscriptionId} ({State}).",
                command.Subscriber.UserId, plan.Handle, created.Id, created.State);

            return Map(created);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListForUserAsync(SubscriberInfo subscriber, CancellationToken cancellationToken)
    {
        var customer = await EnsureCustomerAsync(subscriber, cancellationToken);
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(Map)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberInfo subscriber, CancellationToken cancellationToken)
    {
        var customerReference = CustomerReference(subscriber.UserId);
        var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            _logger.LogInformation(
                "Creating Maxio customer for user {UserId} (reference {Reference}).",
                subscriber.UserId, customerReference);

            return await _client.CreateCustomerAsync(
                new Models.MaxioCreateCustomerRequest
                {
                    Customer = new Models.MaxioCreateCustomer
                    {
                        FirstName = "eShop",
                        LastName = "Subscriber",
                        Email = subscriber.Email,
                        Reference = customerReference
                    }
                },
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            // Concurrent creation race: the reference is unique per site in Maxio, so retry
            // the lookup instead of creating a second customer.
            var raced = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private static SubscriptionPlan? ResolvePlan(IReadOnlyList<SubscriptionPlan> plans, SubscribeCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.PlanHandle))
        {
            return plans.FirstOrDefault(p =>
                string.Equals(p.Handle, command.PlanHandle, StringComparison.OrdinalIgnoreCase));
        }

        if (command.PlanId.HasValue)
        {
            return plans.FirstOrDefault(p => p.Id == command.PlanId.Value);
        }

        return null;
    }

    private static bool IsTerminal(string? state) =>
        string.Equals(state, "canceled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "expired", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "failed_to_create", StringComparison.OrdinalIgnoreCase);

    internal static string CustomerReference(string userId) =>
        $"{CustomerReferencePrefix}{userId}";

    internal static string SubscriptionReference(string userId, string planHandle) =>
        $"{SubscriptionReferencePrefix}{userId}:{planHandle}";

    private static SubscriptionDetails Map(MaxioSubscription subscription)
    {
        var product = subscription.Product;
        return new SubscriptionDetails
        {
            Id = subscription.Id,
            Reference = subscription.Reference ?? string.Empty,
            State = subscription.State ?? string.Empty,
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            Price = subscription.ProductPriceInCents / 100m,
            Currency = subscription.Currency,
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt
        };
    }
}
