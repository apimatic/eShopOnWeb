using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);
}

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();
    private readonly IMaxioBillingClient _maxio;
    private readonly AppIdentityDbContext _identityDbContext;

    public SubscriptionBillingService(IMaxioBillingClient maxio, AppIdentityDbContext identityDbContext)
    {
        _maxio = maxio;
        _identityDbContext = identityDbContext;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.GetProductsAsync(cancellationToken);
        return products
            .Select(product => new SubscriptionPlanDto
            {
                Id = product.Id,
                Handle = product.Handle,
                Name = product.Name,
                Description = product.Description,
                PriceInCents = product.PriceInCents,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit
            })
            .ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        var products = await _maxio.GetProductsAsync(cancellationToken);
        var product = products.SingleOrDefault(item =>
            string.Equals(item.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var gate = UserLocks.GetOrAdd(user.Id, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customerReference = GetCustomerReference(user.Id);
            var subscriptionReference = GetSubscriptionReference(user.Id);
            var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
            var subscription = await EnsureSubscriptionAsync(
                customerReference, subscriptionReference, product.Handle, cancellationToken);

            var existingMapping = await _identityDbContext.MaxioSubscriptionMappings
                .SingleOrDefaultAsync(mapping => mapping.UserId == user.Id, cancellationToken);
            if (existingMapping is null)
            {
                _identityDbContext.MaxioSubscriptionMappings.Add(new MaxioSubscriptionMapping
                {
                    UserId = user.Id,
                    MaxioCustomerId = customer.Id,
                    MaxioSubscriptionId = subscription.Id,
                    SubscriptionReference = subscriptionReference,
                    ProductHandle = product.Handle,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                existingMapping.MaxioCustomerId = customer.Id;
                existingMapping.MaxioSubscriptionId = subscription.Id;
                existingMapping.ProductHandle = product.Handle;
                existingMapping.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _identityDbContext.SaveChangesAsync(cancellationToken);
            return ToDto(subscription, product);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        ApplicationUser user, CancellationToken cancellationToken)
    {
        MaxioCustomer customer;
        try
        {
            customer = await _maxio.GetCustomerByReferenceAsync(
                GetCustomerReference(user.Id), cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(subscription => ToDto(subscription, subscription.Product)).ToArray();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            try
            {
                return await _maxio.CreateCustomerAsync(new MaxioCreateCustomer
                {
                    FirstName = "eShop",
                    LastName = user.UserName ?? user.Id,
                    Email = user.Email ?? user.UserName ?? $"{user.Id}@invalid.local",
                    Reference = reference
                }, cancellationToken);
            }
            catch (MaxioApiException createException) when (
                createException.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                try
                {
                    return await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
                }
                catch (MaxioApiException lookupException) when (lookupException.StatusCode == HttpStatusCode.NotFound)
                {
                    throw createException;
                }
            }
        }
    }

    private async Task<MaxioSubscription> EnsureSubscriptionAsync(
        string customerReference, string subscriptionReference, string productHandle,
        CancellationToken cancellationToken)
    {
        MaxioSubscription? existing = null;
        try
        {
            existing = await _maxio.GetSubscriptionByReferenceAsync(
                subscriptionReference, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            try
            {
                existing = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerReference = customerReference,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = "remittance"
                }, cancellationToken);
            }
            catch (MaxioApiException createException) when (
                createException.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                try
                {
                    existing = await _maxio.GetSubscriptionByReferenceAsync(
                        subscriptionReference, cancellationToken);
                }
                catch (MaxioApiException lookupException) when (lookupException.StatusCode == HttpStatusCode.NotFound)
                {
                    throw createException;
                }
            }
        }

        var actualHandle = existing.Product?.Handle;
        if (!string.IsNullOrWhiteSpace(actualHandle) &&
            !string.Equals(actualHandle, productHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionAlreadyExistsException(actualHandle);
        }

        return existing!;
    }

    private static string GetCustomerReference(string userId) => $"eshop-user-{userId}";
    private static string GetSubscriptionReference(string userId) => $"eshop-subscription-{userId}";

    private static SubscriptionDto ToDto(MaxioSubscription subscription, MaxioProduct? product)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State,
            PlanHandle = subscription.Product?.Handle ?? product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents ?? product?.PriceInCents,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
        };
    }

    private static SubscriptionDto ToDto(MaxioSubscription subscription, MaxioSubscriptionProduct? product)
    {
        return ToDto(subscription, product is null ? null : new MaxioProduct
        {
            Handle = product.Handle,
            Name = product.Name
        });
    }
}

public sealed class SubscriptionPlanDto
{
    public long Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}

public sealed class SubscriptionDto
{
    public long Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long? PriceInCents { get; init; }
    public string State { get; init; } = string.Empty;
    public string? NextBillingDate { get; init; }
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string handle)
        : base($"Subscription plan '{handle}' was not found in the configured Maxio product family.")
    {
    }
}

public sealed class SubscriptionAlreadyExistsException : Exception
{
    public SubscriptionAlreadyExistsException(string existingPlanHandle)
        : base($"The user already has a subscription for plan '{existingPlanHandle}'.")
    {
    }
}
