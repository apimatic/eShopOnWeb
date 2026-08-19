using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public MaxioSubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(ShopperBillingProfile shopper, string productHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionPlanNotFoundException(productHandle ?? string.Empty);
        }

        var handle = productHandle.Trim();
        var gate = _gates.GetOrAdd($"{shopper.Id}:{handle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var plans = await ListPlansAsync(cancellationToken);
            var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                throw new SubscriptionPlanNotFoundException(handle);
            }

            var customer = await GetOrCreateCustomerAsync(shopper, cancellationToken);
            var existing = await FindCurrentSubscriptionAsync(customer.Id!.Value, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for shopper {ShopperId} on plan {PlanHandle}.", existing.Id.GetValueOrDefault(), shopper.Id, plan.Handle);
                return new SubscribeResult(ToCustomerSubscription(existing), created: false);
            }

            var reference = BuildSubscriptionReference(shopper.Id, plan.Handle);
            try
            {
                var created = await _maxio.CreateSubscriptionAsync(
                    new CreateMaxioSubscription(plan.Handle, customer.Id.Value, reference),
                    cancellationToken);

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for shopper {ShopperId} on plan {PlanHandle}.", created.Id.GetValueOrDefault(), shopper.Id, plan.Handle);
                return new SubscribeResult(ToCustomerSubscription(created), created: true);
            }
            catch (MaxioBillingException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
            {
                var raced = await FindCurrentSubscriptionAsync(customer.Id.Value, plan.Handle, cancellationToken)
                            ?? await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
                if (raced is not null)
                {
                    return new SubscribeResult(ToCustomerSubscription(raced), created: false);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(string shopperId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var customer = await _maxio.FindCustomerByReferenceAsync(BuildCustomerReference(shopperId), cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        return subscriptions.Select(ToCustomerSubscription).ToList();
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(ShopperBillingProfile shopper, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(shopper.Id);
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing?.Id is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(shopper);
        try
        {
            return await _maxio.CreateCustomerAsync(
                new CreateMaxioCustomer(firstName, lastName, shopper.Email, reference),
                cancellationToken);
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced?.Id is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindCurrentSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)
            && !IsTerminal(s.State));
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new MaxioConfigurationException("Maxio:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            throw new MaxioConfigurationException("Maxio:Subdomain or Maxio:BaseUrl must be configured.");
        }
    }

    internal static string BuildCustomerReference(string shopperId) => $"eshop:{shopperId}";

    internal static string BuildSubscriptionReference(string shopperId, string productHandle) =>
        $"eshop:{shopperId}:{productHandle}";

    private static bool IsTerminal(string? state) =>
        !string.IsNullOrWhiteSpace(state) && TerminalStates.Contains(state);

    private static (string FirstName, string LastName) SplitName(ShopperBillingProfile shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.UserName) ? shopper.UserName! : shopper.Email;
        var local = source.Contains('@', StringComparison.Ordinal)
            ? source[..source.IndexOf('@')]
            : source;
        if (string.IsNullOrWhiteSpace(local))
        {
            local = "Shopper";
        }

        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return (textInfo.ToTitleCase(local.Replace('.', ' ').Replace('_', ' ')), "eShopOnWeb");
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) =>
        new(
            product.Handle!,
            string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name!,
            product.Description,
            ToPrice(product.PriceInCents),
            product.Interval ?? 1,
            product.IntervalUnit ?? "month");

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription) =>
        new(
            subscription.Id ?? 0,
            subscription.State ?? "unknown",
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            ToPrice(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
            subscription.NextAssessmentAt);

    private static decimal ToPrice(long? priceInCents) =>
        priceInCents.HasValue ? priceInCents.Value / 100m : 0m;
}
