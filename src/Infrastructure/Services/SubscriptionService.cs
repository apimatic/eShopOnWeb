using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Implements recurring-subscription flows against Maxio Advanced Billing as the system of record.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(IMaxioClient maxioClient, IOptions<MaxioOptions> options, ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await ResolveProductFamilyAsync(cancellationToken);
        var products = await _maxioClient.ListProductsAsync(family.Id, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan(
                p.Id,
                p.Handle,
                p.Name,
                p.Description,
                ToDecimal(p.PriceInCents),
                p.Interval,
                p.IntervalUnit))
            .ToList();
    }

    public async Task<Subscription> SubscribeAsync(string customerReference, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(customerReference));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var customer = await EnsureCustomerAsync(customerReference, cancellationToken);

        var existing = await FindActiveSubscriptionAsync(customer.Id, planHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Customer {CustomerReference} already has an active subscription to plan {PlanHandle}; returning existing subscription {SubscriptionId}.",
                customerReference, planHandle, existing.Id);
            return Map(existing);
        }

        var created = await _maxioClient.CreateSubscriptionAsync(
            new CreateSubscriptionRequest(customer.Id, planHandle), cancellationToken);

        _logger.LogInformation("Created Maxio subscription {SubscriptionId} for customer {CustomerReference} on plan {PlanHandle}.",
            created.Id, customerReference, planHandle);

        return Map(created);
    }

    public async Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return Array.Empty<Subscription>();
        }

        var customer = await _maxioClient.GetCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioProductFamily> ResolveProductFamilyAsync(CancellationToken cancellationToken)
    {
        var family = await _maxioClient.GetProductFamilyByHandleAsync(_options.ProductFamilyHandle, cancellationToken);
        if (family is null)
        {
            throw new MaxioApiException(
                HttpStatusCode.NotFound,
                $"No Maxio product family was found with handle '{_options.ProductFamilyHandle}'.");
        }

        return family;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string customerReference, CancellationToken cancellationToken)
    {
        var customer = await _maxioClient.GetCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = SplitName(customerReference);
        var created = await _maxioClient.CreateCustomerAsync(
            new CreateCustomerRequest(firstName, lastName, customerReference, customerReference), cancellationToken);

        _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.", created.Id, customerReference);
        return created;
    }

    private async Task<MaxioSubscription?> FindActiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(s.State, "active", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(s.State, "trialing", StringComparison.OrdinalIgnoreCase)));
    }

    private static (string FirstName, string LastName) SplitName(string email)
    {
        int at = email.IndexOf('@');
        string local = at > 0 ? email[..at] : email;
        return (local, "eShopOnWeb User");
    }

    private static Subscription Map(MaxioSubscription source)
    {
        return new Subscription(
            source.Id,
            source.State,
            source.Product.Handle,
            source.Product.Name,
            ToDecimal(source.ProductPriceInCents),
            source.CurrentPeriodEndsAt,
            source.NextAssessmentAt,
            source.CreatedAt,
            source.ActivatedAt,
            source.CanceledAt,
            source.PaymentCollectionMethod);
    }

    private static decimal ToDecimal(int cents) => cents / 100m;
}
