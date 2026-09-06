using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscribe flow: resolve the plan, make sure the shopper has a billing customer,
/// then enroll them exactly once.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingGateway _billingGateway;
    private readonly KeyedAsyncLock _subscriberLock;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IBillingGateway billingGateway,
        KeyedAsyncLock subscriberLock,
        IAppLogger<SubscriptionService> logger)
    {
        _billingGateway = billingGateway;
        _subscriberLock = subscriberLock;
        _logger = logger;
    }

    public Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _billingGateway.ListPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request, nameof(request));
        Guard.Against.NullOrWhiteSpace(request.UserName, nameof(request.UserName));
        Guard.Against.NullOrWhiteSpace(request.PlanHandle, nameof(request.PlanHandle));

        var plan = await ResolvePlanAsync(request.PlanHandle, cancellationToken);
        var reference = BillingCustomerReference.For(request.UserName);

        // Serialise per shopper so a double-click cannot get two creates in flight at once.
        using (await _subscriberLock.AcquireAsync(reference, cancellationToken))
        {
            var customer = await EnsureCustomerAsync(request, reference, cancellationToken);

            var subscriptions = await _billingGateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = FindLiveSubscription(subscriptions, plan.Handle);
            if (existing is not null)
            {
                _logger.LogInformation("Customer {CustomerId} is already subscribed to plan {PlanHandle} (subscription {SubscriptionId}, state {State}); returning the existing subscription.",
                    customer.Id, plan.Handle, existing.Id, existing.State);
                return new SubscribeResult(existing, plan, alreadySubscribed: true);
            }

            var newSubscription = new NewSubscription
            {
                CustomerId = customer.Id,
                PlanHandle = plan.Handle,
                UniquenessToken = BuildUniquenessToken(reference, plan.Handle, request.IdempotencyKey)
            };

            BillingSubscription created;
            try
            {
                created = await _billingGateway.CreateSubscriptionAsync(newSubscription, cancellationToken);
            }
            catch (BillingConflictException ex)
            {
                // The caller replayed an idempotency key the billing system has already seen, so an
                // earlier attempt got through. Re-read and hand back whatever it produced.
                _logger.LogWarning("Subscribe for customer {CustomerId} on plan {PlanHandle} was rejected as a duplicate: {Message}. Re-reading the customer's subscriptions.",
                    customer.Id, plan.Handle, ex.Message);

                var afterConflict = await _billingGateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                var winner = FindLiveSubscription(afterConflict, plan.Handle);
                if (winner is null)
                {
                    throw;
                }

                return new SubscribeResult(winner, plan, alreadySubscribed: true);
            }

            _logger.LogInformation("Created subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}; state {State}, next billing {NextBillingAt}.",
                created.Id, customer.Id, plan.Handle, created.State, created.NextBillingAt?.ToString("o") ?? "unscheduled");

            return new SubscribeResult(created, plan, alreadySubscribed: false);
        }
    }

    public async Task<IReadOnlyCollection<BillingSubscription>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var reference = BillingCustomerReference.For(userName);
        var customer = await _billingGateway.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        var subscriptions = await _billingGateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.OrderByDescending(subscription => subscription.CreatedAt).ToList();
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await _billingGateway.ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(candidate => string.Equals(candidate.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

        return plan ?? throw new SubscriptionPlanNotFoundException(planHandle, plans.Select(candidate => candidate.Handle));
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(SubscribeRequest request, string reference, CancellationToken cancellationToken)
    {
        var existing = await _billingGateway.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ResolveName(request);
        try
        {
            var created = await _billingGateway.CreateCustomerAsync(new NewBillingCustomer
            {
                Reference = reference,
                FirstName = firstName,
                LastName = lastName,
                Email = request.UserName,
                Organization = request.Organization
            }, cancellationToken);

            _logger.LogInformation("Created billing customer {CustomerId} for reference {Reference}.", created.Id, reference);
            return created;
        }
        catch (BillingConflictException)
        {
            // Someone else created this customer between the lookup and the create. Their record is
            // the one to use.
            var winner = await _billingGateway.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            _logger.LogInformation("Billing customer {CustomerId} for reference {Reference} was created concurrently; using it.", winner.Id, reference);
            return winner;
        }
    }

    private static BillingSubscription? FindLiveSubscription(IEnumerable<BillingSubscription> subscriptions, string planHandle)
        => subscriptions.FirstOrDefault(subscription =>
            subscription.IsLive && string.Equals(subscription.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Builds the duplicate-prevention token sent with the create.
    /// </summary>
    /// <remarks>
    /// One token per subscribe attempt, so the gateway can safely re-send the create after a
    /// timeout without risking two subscriptions. It is deliberately not derived from the shopper
    /// and plan alone: the billing system remembers a token for an hour whether the create
    /// succeeded or failed, so a shared token would lock the shopper out of retrying after a
    /// failure they have since fixed. A caller who wants a replayable request supplies its own
    /// idempotency key, and the same key always yields the same token.
    /// </remarks>
    private static string BuildUniquenessToken(string reference, string planHandle, string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Guid.NewGuid().ToString("N");
        }

        var seed = $"{reference}|{planHandle}|{idempotencyKey.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
    }

    /// <summary>
    /// The billing system requires a first and last name, but an eShopOnWeb account only carries an
    /// email address. Callers can supply real names on the request; otherwise they are derived from
    /// the local part of the email.
    /// </summary>
    private static (string FirstName, string LastName) ResolveName(SubscribeRequest request)
    {
        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();
        if (!string.IsNullOrEmpty(firstName) && !string.IsNullOrEmpty(lastName))
        {
            return (firstName, lastName);
        }

        var localPart = request.UserName.Split('@')[0];
        var words = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Capitalize)
            .ToArray();

        var derivedFirst = words.Length > 0 ? words[0] : "eShopOnWeb";
        var derivedLast = words.Length > 1 ? string.Join(" ", words.Skip(1)) : derivedFirst;

        return (string.IsNullOrEmpty(firstName) ? derivedFirst : firstName,
                string.IsNullOrEmpty(lastName) ? derivedLast : lastName);
    }

    private static string Capitalize(string word)
        => word.Length <= 1 ? word.ToUpperInvariant() : char.ToUpperInvariant(word[0]) + word.Substring(1);
}
