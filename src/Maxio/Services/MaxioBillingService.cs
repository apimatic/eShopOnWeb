using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Maxio.Configuration;
using Microsoft.eShopWeb.Maxio.Contracts;
using Microsoft.eShopWeb.Maxio.Http;

namespace Microsoft.eShopWeb.Maxio.Services;

/// <summary>
/// Maxio-backed implementation of the subscription capability.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    private static readonly HashSet<string> EndStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired"
    };

    // Subscriptions without a payment method are collected on remittance terms (invoice),
    // which is what lets a shopper subscribe without card capture / 3-DS.
    private const string RemittanceCollectionMethod = "remittance";

    // A create-then-check is not atomic at the Maxio API level: Maxio does not enforce unique
    // customer references, so a concurrent double-submit could otherwise create duplicate
    // customers/subscriptions. Serializing per customer closes that race in-process.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CustomerLocks = new();

    private readonly IMaxioApiClient _client;
    private readonly MaxioOptions _options;

    public MaxioBillingService(IMaxioApiClient client, MaxioOptions options)
    {
        _client = client;
        _options = options;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var family = await GetConfiguredProductFamilyAsync(cancellationToken).ConfigureAwait(false);
        var familyId = family.Id ?? throw new MaxioConfigurationException(
            $"Maxio product family '{_options.ProductFamilyHandle}' was found but has no id.");

        var site = await _client.GetSiteAsync(cancellationToken).ConfigureAwait(false);
        var products = await _client.ListProductsByFamilyAsync(familyId, cancellationToken).ConfigureAwait(false);
        string currency = site.Currency ?? string.Empty;

        var plans = new List<SubscriptionPlan>();
        foreach (var product in products)
        {
            if (product.ArchivedAt is not null || string.IsNullOrEmpty(product.Handle))
            {
                continue;
            }

            plans.Add(new SubscriptionPlan
            {
                Handle = product.Handle,
                Name = product.Name ?? string.Empty,
                Description = product.Description,
                Price = CentsToAmount(product.PriceInCents) ?? 0,
                Interval = product.Interval ?? 0,
                IntervalUnit = product.IntervalUnit ?? string.Empty,
                Currency = currency,
                RequiresCreditCard = product.RequireCreditCard,
                TrialInterval = product.TrialInterval,
                TrialIntervalUnit = product.TrialIntervalUnit,
                InitialCharge = CentsToAmount(product.InitialChargeInCents)
            });
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.CustomerReference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.ProductHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(command));
        }

        var plan = await FindPlanAsync(command.ProductHandle, cancellationToken).ConfigureAwait(false);
        if (plan is null)
        {
            throw new PlanNotFoundException(command.ProductHandle, _options.ProductFamilyHandle);
        }

        // Scope the whole ensure-customer -> find-existing -> create under the customer's lock.
        SemaphoreSlim gate = CustomerLocks.GetOrAdd(command.CustomerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var customer = await EnsureCustomerAsync(command, cancellationToken).ConfigureAwait(false);
            var customerId = customer.Id ?? throw new MaxioConfigurationException(
                "Maxio customer was created but the response contained no id.");

            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken).ConfigureAwait(false);
            var existing = subscriptions.FirstOrDefault(subscription =>
                string.Equals(subscription.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                !EndStates.Contains(subscription.State ?? string.Empty));

            if (existing is not null)
            {
                return new SubscribeResult(ToRecord(existing), created: false);
            }

            var created = await _client.CreateSubscriptionAsync(new MaxioNewSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customerId,
                PaymentCollectionMethod = RemittanceCollectionMethod
            }, cancellationToken).ConfigureAwait(false);

            return new SubscribeResult(ToRecord(created), created: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionRecord>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken)
    {
        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken).ConfigureAwait(false);
        if (customer?.Id is null)
        {
            return Array.Empty<SubscriptionRecord>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken).ConfigureAwait(false);
        return subscriptions.Select(ToRecord).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(command.CustomerReference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ResolveCustomerName(command.FirstName, command.LastName, command.Email);
        var created = await _client.CreateCustomerAsync(new MaxioNewCustomer
        {
            FirstName = firstName,
            LastName = lastName,
            Email = command.Email,
            Reference = command.CustomerReference
        }, cancellationToken).ConfigureAwait(false);

        if (created.Id is null)
        {
            throw new MaxioConfigurationException("Maxio customer was created but the response contained no id.");
        }

        return created;
    }

    private async Task<MaxioProduct?> FindPlanAsync(string handle, CancellationToken cancellationToken)
    {
        var family = await GetConfiguredProductFamilyAsync(cancellationToken).ConfigureAwait(false);
        if (family.Id is null)
        {
            return null;
        }

        var products = await _client.ListProductsByFamilyAsync(family.Id.Value, cancellationToken).ConfigureAwait(false);
        return products.FirstOrDefault(product =>
            product.ArchivedAt is null &&
            string.Equals(product.Handle, handle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<MaxioProductFamily> GetConfiguredProductFamilyAsync(CancellationToken cancellationToken)
    {
        var families = await _client.ListProductFamiliesAsync(cancellationToken).ConfigureAwait(false);
        var family = families.FirstOrDefault(item =>
            string.Equals(item.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family is null)
        {
            throw new MaxioConfigurationException(
                $"Maxio product family '{_options.ProductFamilyHandle}' was not found on site '{_options.Subdomain}'.");
        }

        return family;
    }

    private static SubscriptionRecord ToRecord(MaxioSubscription subscription)
    {
        var product = subscription.Product;
        long priceInCents = subscription.ProductPriceInCents ?? product?.PriceInCents ?? 0;

        return new SubscriptionRecord
        {
            Id = subscription.Id ?? 0,
            State = subscription.State ?? string.Empty,
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            Price = CentsToAmount(priceInCents),
            Interval = product?.Interval ?? 0,
            IntervalUnit = product?.IntervalUnit ?? string.Empty,
            Currency = subscription.Currency ?? string.Empty,
            CustomerId = subscription.Customer?.Id ?? 0,
            CustomerReference = subscription.Customer?.Reference ?? string.Empty,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            Balance = CentsToAmount(subscription.BalanceInCents) ?? 0
        };
    }

    private static decimal? CentsToAmount(long? cents) => cents is null ? null : cents.Value / 100m;

    private static decimal CentsToAmount(long cents) => cents / 100m;

    private static (string FirstName, string LastName) ResolveCustomerName(string? firstName, string? lastName, string email)
    {
        string emailLocalPart = email;
        string emailDomain = string.Empty;
        int at = email.IndexOf('@');
        if (at >= 0)
        {
            emailLocalPart = email[..at];
            emailDomain = email[(at + 1)..];
        }

        string first = string.IsNullOrWhiteSpace(firstName) ? emailLocalPart : firstName.Trim();

        string last;
        if (!string.IsNullOrWhiteSpace(lastName))
        {
            last = lastName.Trim();
        }
        else
        {
            int dot = emailLocalPart.IndexOf('.');
            if (dot > 0 && dot < emailLocalPart.Length - 1)
            {
                last = emailLocalPart[(dot + 1)..];
            }
            else if (!string.IsNullOrEmpty(emailDomain))
            {
                int domainDot = emailDomain.IndexOf('.');
                last = domainDot > 0 ? emailDomain[..domainDot] : emailDomain;
            }
            else
            {
                last = "User";
            }
        }

        return (first, last);
    }
}
