using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maps Maxio Advanced Billing onto the eShopOnWeb billing port: wire models become domain models,
/// and Maxio transport failures become the provider-neutral exceptions the application layer knows.
/// </summary>
public class MaxioBillingGateway : IBillingGateway
{
    private readonly IMaxioApiClient _client;
    private readonly MaxioSiteCache _siteCache;
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(
        IMaxioApiClient client,
        MaxioSiteCache siteCache,
        IOptionsMonitor<MaxioSettings> settings,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _siteCache = siteCache;
        _settings = settings;
        _logger = logger;
    }

    public Task<BillingSiteInfo> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var lifetime = TimeSpan.FromMinutes(Math.Max(0, _settings.CurrentValue.SiteCacheMinutes));

        return _siteCache.GetOrAddAsync(lifetime, async ct =>
        {
            var site = await Guarded(() => _client.ReadSiteAsync(ct));
            return MapSite(site);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _settings.CurrentValue.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new BillingConfigurationException(
                "Maxio:ProductFamilyHandle is not configured, so there is no catalogue of plans to list.");
        }

        var site = await GetSiteAsync(cancellationToken);
        var products = await Guarded(() => _client.ListProductsForProductFamilyAsync(familyHandle, cancellationToken));

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => MapPlan(p, site.Currency))
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var customer = await Guarded(() => _client.ReadCustomerByReferenceAsync(reference, cancellationToken));
        return customer is null ? null : MapCustomer(customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        var created = await Guarded(() => _client.CreateCustomerAsync(request, cancellationToken));
        return MapCustomer(created);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await Guarded(() => _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken));
        return subscriptions.Select(MapSubscription).ToList();
    }

    public async Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var subscription = await Guarded(() => _client.FindSubscriptionAsync(reference, cancellationToken));
        return subscription is null ? null : MapSubscription(subscription);
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(NewSubscription subscription, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = subscription.PlanHandle,
                CustomerId = subscription.CustomerId,
                Reference = subscription.Reference,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod
            }
        };

        var created = await Guarded(() => _client.CreateSubscriptionAsync(request, cancellationToken));
        return MapSubscription(created);
    }

    /// <summary>
    /// Runs a client call and translates Maxio-specific failures into the provider-neutral
    /// exceptions declared in ApplicationCore, so nothing above this layer has to know about Maxio.
    /// </summary>
    private async Task<T> Guarded<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex);
        }
        catch (MaxioTransportException ex)
        {
            _logger.LogError(ex, "Maxio {Method} {Path} could not be completed.", ex.Method, ex.RequestPath);
            throw new BillingProviderUnavailableException("The billing provider is not reachable right now.", innerException: ex);
        }
    }

    private BillingProviderException Translate(MaxioApiException ex)
    {
        switch (ex.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                _logger.LogError(ex, "Maxio rejected the configured credentials for {Method} {Path}.", ex.Method, ex.RequestPath);
                return new BillingConfigurationException(
                    "The billing provider rejected the configured credentials. Check Maxio:ApiKey and Maxio:Subdomain.",
                    ex.Errors, ex);

            case HttpStatusCode.NotFound:
                // A 404 that reached here is on a path we address by configuration, so the
                // configured site or product family does not exist.
                _logger.LogError(ex, "Maxio has no resource at {Method} {Path}.", ex.Method, ex.RequestPath);
                return new BillingConfigurationException(
                    "The billing provider has no such resource. Check Maxio:Subdomain and Maxio:ProductFamilyHandle.",
                    ex.Errors, ex);

            case HttpStatusCode.BadRequest:
            case HttpStatusCode.UnprocessableEntity:
            case HttpStatusCode.Conflict:
                _logger.LogWarning("Maxio rejected {Method} {Path}: {Errors}", ex.Method, ex.RequestPath, string.Join(" ", ex.Errors));
                return new BillingRequestRejectedException("The billing provider rejected the request.", ex.Errors, ex);

            default:
                _logger.LogError(ex, "Maxio {Method} {Path} failed with {StatusCode}.", ex.Method, ex.RequestPath, (int)ex.StatusCode);
                return new BillingProviderUnavailableException(
                    "The billing provider could not complete the request.", ex.Errors, ex);
        }
    }

    private static BillingSiteInfo MapSite(MaxioSite site) => new(
        site.Id,
        site.Name,
        site.Subdomain,
        site.Currency,
        site.RelationshipInvoicingEnabled,
        site.DefaultPaymentCollectionMethod,
        site.Test);

    private static SubscriptionPlan MapPlan(MaxioProduct product, string? siteCurrency) => new(
        handle: product.Handle!,
        name: product.Name ?? product.Handle!,
        description: product.Description,
        priceInCents: product.PriceInCents,
        currency: siteCurrency,
        interval: product.Interval,
        intervalUnit: product.IntervalUnit,
        requiresPaymentMethod: product.RequireCreditCard,
        productFamilyHandle: product.ProductFamily?.Handle,
        productFamilyName: product.ProductFamily?.Name,
        trialPriceInCents: product.TrialPriceInCents,
        trialInterval: product.TrialInterval,
        trialIntervalUnit: product.TrialIntervalUnit,
        expirationInterval: product.ExpirationInterval,
        expirationIntervalUnit: product.ExpirationIntervalUnit,
        initialChargeInCents: product.InitialChargeInCents,
        taxable: product.Taxable,
        pricePointName: product.ProductPricePointName);

    private static BillingCustomer MapCustomer(MaxioCustomer customer) => new(
        customer.Id,
        customer.Reference,
        customer.FirstName,
        customer.LastName,
        customer.Email);

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new(
        id: subscription.Id,
        reference: subscription.Reference,
        state: MaxioSubscriptionStates.Parse(subscription.State),
        rawState: subscription.State,
        planHandle: subscription.Product?.Handle,
        planName: subscription.Product?.Name,
        productPriceInCents: subscription.ProductPriceInCents,
        currency: subscription.Currency,
        interval: subscription.Product?.Interval,
        intervalUnit: subscription.Product?.IntervalUnit,
        customerId: subscription.Customer?.Id ?? 0,
        customerReference: subscription.Customer?.Reference,
        currentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
        currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
        nextAssessmentAt: subscription.NextAssessmentAt,
        trialStartedAt: subscription.TrialStartedAt,
        trialEndedAt: subscription.TrialEndedAt,
        activatedAt: subscription.ActivatedAt,
        canceledAt: subscription.CanceledAt,
        expiresAt: subscription.ExpiresAt,
        createdAt: subscription.CreatedAt,
        cancelAtEndOfPeriod: subscription.CancelAtEndOfPeriod,
        paymentCollectionMethod: subscription.PaymentCollectionMethod,
        balanceInCents: subscription.BalanceInCents);
}
