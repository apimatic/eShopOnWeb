using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new(StringComparer.Ordinal);

    private readonly IMaxioBillingGateway _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioBillingGateway maxio,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<SubscriptionPlan>>> ListPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            var plans = await _maxio.ListAvailablePlansAsync(cancellationToken);
            return Result<IReadOnlyList<SubscriptionPlan>>.Success(plans);
        }
        catch (MaxioConfigurationException ex)
        {
            _logger.LogWarning("Maxio is not configured: {Message}", ex.Message);
            return Result<IReadOnlyList<SubscriptionPlan>>.Error(ex.Message);
        }
        catch (MaxioBillingException ex)
        {
            _logger.LogWarning("Failed to list Maxio plans: {Message}", ex.Message);
            return Result<IReadOnlyList<SubscriptionPlan>>.Error(ex.Message);
        }
    }

    public async Task<Result<ShopperSubscription>> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        Guard.Against.Null(shopper);
        Guard.Against.NullOrEmpty(shopper.UserId);

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            return Result<ShopperSubscription>.Invalid(new List<ValidationError>
            {
                new()
                {
                    Identifier = nameof(productHandle),
                    ErrorMessage = "A product handle is required."
                }
            });
        }

        productHandle = productHandle.Trim();

        try
        {
            var plan = await _maxio.GetPlanByHandleAsync(productHandle, cancellationToken);
            if (plan is null)
            {
                return Result<ShopperSubscription>.NotFound($"No subscription plan with handle '{productHandle}' is available.");
            }

            var lockKey = $"{shopper.UserId}:{productHandle}";
            var gate = SubscribeLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await EnrollLockedAsync(shopper, plan, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }
        catch (MaxioConfigurationException ex)
        {
            _logger.LogWarning("Maxio is not configured: {Message}", ex.Message);
            return Result<ShopperSubscription>.Error(ex.Message);
        }
        catch (MaxioBillingException ex)
        {
            _logger.LogWarning("Failed to subscribe shopper {UserId} to {Handle}: {Message}", shopper.UserId, productHandle, ex.Message);
            return Result<ShopperSubscription>.Error(ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<ShopperSubscription>>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken)
    {
        Guard.Against.Null(shopper);
        Guard.Against.NullOrEmpty(shopper.UserId);

        try
        {
            var customer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(shopper), cancellationToken);
            if (customer is null)
            {
                return Result<IReadOnlyList<ShopperSubscription>>.Success(Array.Empty<ShopperSubscription>());
            }

            var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            return Result<IReadOnlyList<ShopperSubscription>>.Success(subscriptions);
        }
        catch (MaxioConfigurationException ex)
        {
            _logger.LogWarning("Maxio is not configured: {Message}", ex.Message);
            return Result<IReadOnlyList<ShopperSubscription>>.Error(ex.Message);
        }
        catch (MaxioBillingException ex)
        {
            _logger.LogWarning("Failed to list subscriptions for shopper {UserId}: {Message}", shopper.UserId, ex.Message);
            return Result<IReadOnlyList<ShopperSubscription>>.Error(ex.Message);
        }
    }

    private async Task<Result<ShopperSubscription>> EnrollLockedAsync(
        ShopperIdentity shopper,
        SubscriptionPlan plan,
        CancellationToken cancellationToken)
    {
        var customer = await EnsureCustomerAsync(shopper, cancellationToken);
        var existing = await FindOpenSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Shopper {UserId} already has Maxio subscription {SubscriptionId} on {Handle} ({State}); returning existing enrollment.",
                shopper.UserId, existing.Id, plan.Handle, existing.State);
            return Result<ShopperSubscription>.Success(existing);
        }

        var reference = SubscriptionReference(shopper, plan.Handle);
        var uniquenessToken = Guid.NewGuid().ToString("N");
        // Products that do not require a card use remittance so the first assessment
        // does not fail with "No payment method was on file". See Create Subscription
        // payment_collection_method and Subscription signup "Payment Methods".
        var collectionMethod = plan.RequireCreditCard ? null : "remittance";

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(
                new NewBillingSubscription(plan.Handle, customer.Id, reference, uniquenessToken, collectionMethod),
                cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for shopper {UserId} on {Handle} in state {State}.",
                created.Id, shopper.UserId, plan.Handle, created.State);

            return Result<ShopperSubscription>.Success(created);
        }
        catch (MaxioBillingException ex) when (ex.StatusCode is HttpStatusCode.Conflict
                                               or HttpStatusCode.UnprocessableEntity)
        {
            var recovered = await RecoverExistingSubscriptionAsync(customer.Id, plan.Handle, reference, cancellationToken);
            if (recovered is not null)
            {
                _logger.LogInformation(
                    "Recovered existing Maxio subscription {SubscriptionId} for shopper {UserId} on {Handle} after {Status}.",
                    recovered.Id, shopper.UserId, plan.Handle, (int)ex.StatusCode);
                return Result<ShopperSubscription>.Success(recovered);
            }

            throw;
        }
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(shopper);
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var names = SplitDisplayName(shopper);
        try
        {
            var created = await _maxio.CreateCustomerAsync(
                new NewBillingCustomer(reference, shopper.Email, names.FirstName, names.LastName),
                uniquenessToken: $"eshop-cust-{shopper.UserId}",
                cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for shopper {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioBillingException ex) when (ex.StatusCode is HttpStatusCode.Conflict
                                               or HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<ShopperSubscription?> FindOpenSubscriptionAsync(
        long customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase)
            && SubscriptionStates.IsOpen(s.State));
    }

    private async Task<ShopperSubscription?> RecoverExistingSubscriptionAsync(
        long customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        var byReference = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (byReference is not null && SubscriptionStates.IsOpen(byReference.State))
        {
            return byReference;
        }

        return await FindOpenSubscriptionAsync(customerId, productHandle, cancellationToken);
    }

    internal static string CustomerReference(ShopperIdentity shopper) => $"eshop-user:{shopper.UserId}";

    internal static string SubscriptionReference(ShopperIdentity shopper, string productHandle) =>
        $"eshop-sub:{shopper.UserId}:{productHandle}";

    internal static (string FirstName, string LastName) SplitDisplayName(ShopperIdentity shopper)
    {
        var source = shopper.UserName;
        if (!string.IsNullOrWhiteSpace(shopper.Email))
        {
            source = shopper.Email;
        }

        var local = source.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Capitalize(parts[1]) : "Customer";
        return (first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }
}
