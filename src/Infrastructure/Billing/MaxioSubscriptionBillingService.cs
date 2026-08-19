using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "cancelled",
        "expired",
        "failed",
        "trial_ended"
    };

    private readonly MaxioAdvancedBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient maxio,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
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
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriptionSummary> SubscribeAsync(
        SubscribeToPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new BillingValidationException("A signed-in user is required to subscribe.");
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            throw new BillingValidationException("productHandle is required.");
        }

        var handle = request.ProductHandle.Trim();
        await EnsurePlanExistsAsync(handle, cancellationToken);

        var gateKey = $"{request.UserId}:{handle}";
        var gate = SubscribeGates.GetOrAdd(gateKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(request, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id!.Value, handle, cancellationToken);
            if (existing is not null)
            {
                return MapSubscription(existing);
            }

            var reference = SubscriptionReference(request.UserId, handle);
            try
            {
                var created = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscription
                {
                    ProductHandle = handle,
                    CustomerId = customer.Id,
                    Reference = reference,
                    PaymentCollectionMethod = "remittance"
                }, cancellationToken);
                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for product {ProductHandle}.", created.Id, handle);
                return MapSubscription(created);
            }
            catch (BillingValidationException)
            {
                var raced = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
                if (raced is not null && IsLive(raced.State))
                {
                    return MapSubscription(raced);
                }

                raced = await FindLiveSubscriptionAsync(customer.Id.Value, handle, cancellationToken);
                if (raced is not null)
                {
                    return MapSubscription(raced);
                }

                var retry = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscription
                {
                    ProductHandle = handle,
                    CustomerId = customer.Id,
                    Reference = $"{reference}:{Guid.NewGuid():N}",
                    PaymentCollectionMethod = "remittance"
                }, cancellationToken);
                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for product {ProductHandle}.", retry.Id, handle);
                return MapSubscription(retry);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListUserSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new BillingValidationException("A signed-in user is required.");
        }

        var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task EnsurePlanExistsAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(plan => string.Equals(plan.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BillingNotFoundException($"No subscription plan with handle '{productHandle}' is available.");
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        SubscribeToPlanRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(request.UserId, cancellationToken);
        if (existing?.Id is not null)
        {
            return existing;
        }

        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCustomer
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Reference = request.UserId
            }, cancellationToken);
        }
        catch (BillingValidationException ex) when (IsDuplicateCustomer(ex))
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(request.UserId, cancellationToken);
            if (raced?.Id is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(
        long customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            IsLive(subscription.State) &&
            string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new BillingUnavailableException("Maxio:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingUnavailableException("Maxio:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            throw new BillingUnavailableException("Set Maxio:BaseUrl or Maxio:Subdomain.");
        }
    }

    private static bool IsDuplicateCustomer(BillingValidationException exception)
    {
        var message = exception.Message;
        return message.Contains("reference", StringComparison.OrdinalIgnoreCase)
               && (message.Contains("taken", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("unique", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("already", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state);

    private static string SubscriptionReference(string userId, string productHandle) =>
        $"{userId}:{productHandle}";

    private static SubscriptionPlan MapPlan(MaxioProduct product) =>
        new()
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            Price = ToMoney(product.PriceInCents),
            Interval = product.Interval ?? 0,
            IntervalUnit = product.IntervalUnit ?? string.Empty
        };

    private static SubscriptionSummary MapSubscription(MaxioSubscription subscription) =>
        new()
        {
            Id = subscription.Id ?? 0,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            Price = ToMoney(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
            State = subscription.State ?? string.Empty,
            NextBillingAt = subscription.NextAssessmentAt
        };

    private static decimal ToMoney(long? amountInCents) =>
        amountInCents.HasValue ? amountInCents.Value / 100m : 0m;
}
