using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the subscription-billing use cases against Maxio Advanced Billing:
/// listing plans, idempotently ensuring a customer, and double-click-safe subscribe.
/// Maps Maxio wire models to the provider-agnostic domain models the app consumes.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    /// <summary>
    /// End-of-life subscription states. A subscription in one of these is considered spent,
    /// so a fresh subscribe to the same plan is allowed; any other state is reused (idempotency).
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    /// <summary>
    /// Per-reference locks so that concurrent subscribe calls for the same user (e.g. a
    /// double-click) are serialized within this process. Combined with the "reuse existing
    /// subscription" check below, this prevents duplicate customers/subscriptions.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ReferenceLocks = new();

    private readonly MaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioApiClient client, IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var products = await _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(userReference))
        {
            throw new ArgumentException("A user reference is required.", nameof(userReference));
        }

        var customer = await _client.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(MapSubscription)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.UserReference))
        {
            throw new ArgumentException("A user reference is required to subscribe.", nameof(command));
        }

        // Resolve the target plan from the live catalog. Verifies the requested handle exists,
        // or picks a sensible default (cheapest plan) when none was supplied.
        var plans = await GetPlansAsync(cancellationToken);
        if (plans.Count == 0)
        {
            throw new BillingProviderException(
                $"No subscription plans are available in product family '{_settings.ProductFamilyHandle}'.");
        }

        SubscriptionPlan targetPlan;
        if (!string.IsNullOrWhiteSpace(command.PlanHandle))
        {
            targetPlan = plans.FirstOrDefault(p => string.Equals(p.Handle, command.PlanHandle, StringComparison.OrdinalIgnoreCase))
                ?? throw new PlanNotFoundException(command.PlanHandle!);
        }
        else
        {
            targetPlan = plans[0];
            _logger.LogInformation("No plan handle supplied; defaulting to '{Handle}'.", targetPlan.Handle);
        }

        var gate = ReferenceLocks.GetOrAdd(command.UserReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(command, cancellationToken);

            // Idempotency: if a non-terminal subscription to this plan already exists, return it.
            var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var reusable = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, targetPlan.Handle, StringComparison.OrdinalIgnoreCase)
                && !TerminalStates.Contains(s.State ?? string.Empty));

            if (reusable is not null)
            {
                _logger.LogInformation(
                    "Reusing existing subscription {SubscriptionId} ({State}) for customer {CustomerId} on plan '{Handle}'.",
                    reusable.Id, reusable.State, customer.Id, targetPlan.Handle);
                return new SubscribeResult { Subscription = MapSubscription(reusable), AlreadyExisted = true };
            }

            var created = await _client.CreateSubscriptionAsync(new CreateSubscription
            {
                ProductHandle = targetPlan.Handle,
                CustomerId = customer.Id,
                // The plans in scope require no payment method, so bill by invoice (remittance).
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);

            _logger.LogInformation(
                "Created subscription {SubscriptionId} for customer {CustomerId} on plan '{Handle}'.",
                created.Id, customer.Id, targetPlan.Handle);

            return new SubscribeResult { Subscription = MapSubscription(created), AlreadyExisted = false };
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Returns the Maxio customer for the user, creating one if needed. Idempotent by
    /// <c>reference</c>; tolerates a concurrent create (unique-reference 422) by re-reading.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(command.UserReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(command);
        try
        {
            var created = await _client.CreateCustomerAsync(new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = command.Email,
                Reference = command.UserReference
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for reference '{Reference}'.",
                created.Id, command.UserReference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.UpstreamStatusCode == 422)
        {
            // A concurrent request likely created the customer first (unique reference).
            var reread = await _client.FindCustomerByReferenceAsync(command.UserReference, cancellationToken);
            if (reread is not null)
            {
                return reread;
            }
            throw;
        }
    }

    private void EnsureConfigured()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_settings.ApiKey)) missing.Add("Maxio:ApiKey");
        if (string.IsNullOrWhiteSpace(_settings.Subdomain) && string.IsNullOrWhiteSpace(_settings.BaseUrl))
            missing.Add("Maxio:Subdomain (or Maxio:BaseUrl)");
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle)) missing.Add("Maxio:ProductFamilyHandle");

        if (missing.Count > 0)
        {
            throw new BillingConfigurationException(
                $"Maxio billing is not configured. Missing setting(s): {string.Join(", ", missing)}.");
        }
    }

    /// <summary>
    /// eShopOnWeb identity users carry only an email, so derive a best-effort first/last name.
    /// Maxio requires both on customer creation.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(SubscribeCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.FirstName) || !string.IsNullOrWhiteSpace(command.LastName))
        {
            return (
                string.IsNullOrWhiteSpace(command.FirstName) ? "eShop" : command.FirstName.Trim(),
                string.IsNullOrWhiteSpace(command.LastName) ? "Customer" : command.LastName.Trim());
        }

        var localPart = command.Email.Contains('@', StringComparison.Ordinal)
            ? command.Email[..command.Email.IndexOf('@', StringComparison.Ordinal)]
            : command.Email;

        var first = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart.Trim();
        return (first, "Customer");
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        ProductId = product.Id,
        Name = product.Name ?? product.Handle ?? $"Plan {product.Id}",
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        FormattedPrice = FormatPrice(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static SubscriptionSummary MapSubscription(MaxioSubscription subscription)
    {
        var price = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new SubscriptionSummary
        {
            SubscriptionId = subscription.Id,
            CustomerId = subscription.Customer?.Id ?? 0,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = price,
            FormattedPrice = FormatPrice(price),
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    private static string FormatPrice(long cents) =>
        (cents / 100m).ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
