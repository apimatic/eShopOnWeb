using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the eShopOnWeb subscription capability on top of Maxio Advanced Billing: resolves the
/// eShopOnWeb user, ensures a matching Maxio customer exists (idempotently), enrolls the user in a plan
/// without requiring a payment method, and projects Maxio wire models onto application models.
/// </summary>
internal sealed class MaxioBillingService : IMaxioBillingService
{
    // Maxio subscription states that mean the user already has this plan; re-subscribing to the same
    // plan while in one of these states is a no-op (idempotency guard). Terminal states such as
    // canceled/expired are excluded so a user can re-subscribe after cancelling.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "pending", "assessing"
    };

    // Plans are configured with "payment method not required", so we bill by remittance (invoice)
    // rather than attempting an automatic card charge that would fail without a stored card.
    private const string RemittancePaymentCollection = "remittance";

    private readonly IMaxioApiClient _client;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ReferenceLock _referenceLock;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        IMaxioApiClient client,
        UserManager<ApplicationUser> userManager,
        ReferenceLock referenceLock,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _userManager = userManager;
        _referenceLock = referenceLock;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => p.Handle is not null && string.IsNullOrEmpty(p.ArchivedAt))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, string? pricePointHandle = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioValidationException("A plan handle is required to subscribe.", new[] { "planHandle: is required." });
        }

        var user = await ResolveUserAsync(userName);
        var reference = ResolveReference(user);

        // Serialize concurrent subscribe attempts for the same user so a double-click cannot create
        // two customers or two subscriptions.
        using (await _referenceLock.AcquireAsync(reference, cancellationToken))
        {
            // Validate the requested plan actually belongs to the configured family.
            var products = await _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
            var plan = products.FirstOrDefault(p =>
                string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(p.ArchivedAt));
            if (plan is null)
            {
                throw new SubscriptionPlanNotFoundException(planHandle);
            }

            var customer = await EnsureCustomerAsync(user, reference, cancellationToken);

            // Idempotency: if the user already has a live subscription to this plan, return it.
            var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var alreadyLive = existing.FirstOrDefault(s =>
                s.Product?.Handle is not null &&
                string.Equals(s.Product.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                s.State is not null && LiveStates.Contains(s.State));

            if (alreadyLive is not null)
            {
                _logger.LogInformation(
                    $"User {reference} already has a live subscription ({alreadyLive.Id}) to plan {planHandle}; not creating a duplicate.");
                return new SubscribeResult(MapSubscription(alreadyLive), alreadySubscribed: true, customer.Id);
            }

            var created = await _client.CreateSubscriptionAsync(new CreateSubscriptionBody
            {
                ProductHandle = plan.Handle!,
                ProductPricePointHandle = pricePointHandle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = RemittancePaymentCollection
            }, cancellationToken);

            _logger.LogInformation(
                $"Created Maxio subscription {created.Id} ({created.State}) for user {reference} on plan {planHandle}.");

            return new SubscribeResult(MapSubscription(created), alreadySubscribed: false, customer.Id);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string userName,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userName);
        var reference = ResolveReference(user);

        var customer = await _client.LookupCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    /// <summary>Looks up an existing Maxio customer by reference, creating one if none exists. Idempotent.</summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(user);
        try
        {
            var created = await _client.CreateCustomerAsync(new CreateCustomerBody
            {
                FirstName = firstName,
                LastName = lastName,
                Email = user.Email ?? user.UserName ?? reference,
                Reference = reference
            }, cancellationToken);

            _logger.LogInformation($"Created Maxio customer {created.Id} for user reference {reference}.");
            return created;
        }
        catch (MaxioValidationException ex) when (IsReferenceTaken(ex))
        {
            // The reference-uniqueness constraint rejected the create because another concurrent
            // request created the customer between our lookup and create. Re-read and use it.
            _logger.LogWarning($"Maxio customer for reference {reference} already existed (created concurrently); re-reading.");
            var reread = await _client.LookupCustomerByReferenceAsync(reference, cancellationToken);
            if (reread is not null)
            {
                return reread;
            }
            throw;
        }
    }

    private static bool IsReferenceTaken(MaxioValidationException ex)
        => ex.Errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase)
            && e.Contains("unique", StringComparison.OrdinalIgnoreCase));

    private async Task<ApplicationUser> ResolveUserAsync(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new MaxioApiException("The authenticated user could not be determined from the request.");
        }

        var user = await _userManager.FindByNameAsync(userName);
        return user ?? throw new UserNotFoundException(userName);
    }

    /// <summary>
    /// The stable, unique identifier used as the Maxio customer reference. The eShopOnWeb user name
    /// (email) is used because it is stable across restarts (unlike the in-memory database's user id),
    /// giving restart-safe idempotency for the demo environment.
    /// </summary>
    private static string ResolveReference(ApplicationUser user)
        => user.UserName ?? user.Email ?? user.Id;

    private static (string FirstName, string LastName) DeriveName(ApplicationUser user)
    {
        var email = user.Email ?? user.UserName ?? string.Empty;
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
        return (firstName, "Subscriber");
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new(
        handle: product.Handle!,
        name: product.Name ?? product.Handle!,
        description: product.Description,
        priceInCents: product.PriceInCents,
        formattedPrice: FormatPrice(product.PriceInCents),
        interval: product.Interval,
        intervalUnit: product.IntervalUnit ?? "month",
        productFamilyHandle: product.ProductFamily?.Handle);

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription)
    {
        var priceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new CustomerSubscription(
            id: subscription.Id,
            state: subscription.State ?? "unknown",
            planHandle: subscription.Product?.Handle,
            planName: subscription.Product?.Name,
            priceInCents: priceInCents,
            formattedPrice: FormatPrice(priceInCents),
            interval: subscription.Product?.Interval ?? 0,
            intervalUnit: subscription.Product?.IntervalUnit,
            currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            nextAssessmentAt: subscription.NextAssessmentAt,
            createdAt: subscription.CreatedAt,
            paymentCollectionMethod: subscription.PaymentCollectionMethod);
    }

    private static string FormatPrice(long priceInCents)
        => (priceInCents / 100m).ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
