using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private const string CustomerReferencePrefix = "eshop-user-";
    private const string SubscriptionReferencePrefix = "eshop-subscription-";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppIdentityDbContext _identityDbContext;
    private readonly IMaxioBillingClient _billingClient;
    private readonly IOptions<MaxioOptions> _maxioOptions;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager,
        AppIdentityDbContext identityDbContext,
        IMaxioBillingClient billingClient,
        IOptions<MaxioOptions> maxioOptions,
        ILogger<MaxioSubscriptionService> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
        _identityDbContext = identityDbContext;
        _billingClient = billingClient;
        _maxioOptions = maxioOptions;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync()
    {
        var cancellationToken = RequestCancellationToken;
        var options = GetConfiguredOptions();
        var products = await _billingClient.ListProductsAsync(options.ProductFamilyHandle!, cancellationToken);

        return products.Select(product => new SubscriptionPlanDto
        {
            Handle = product.Handle,
            Name = product.Name,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = product.PriceInCents / 100m,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit
        }).ToList();
    }

    public async Task<SubscriptionDto?> SubscribeAsync(string? planHandle)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var cancellationToken = RequestCancellationToken;
        var options = GetConfiguredOptions();
        var user = await GetCurrentUserAsync();
        var products = await _billingClient.ListProductsAsync(options.ProductFamilyHandle!, cancellationToken);
        var product = products.FirstOrDefault(item => string.Equals(item.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            return null;
        }

        var customerReference = CustomerReferencePrefix + user.Id;
        var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
        var subscriptionReference = $"{SubscriptionReferencePrefix}{user.Id}-{product.Handle}";

        // The reference is globally unique in Advanced Billing. Looking it up first makes
        // retries and double-clicks return the original subscription without creating another.
        var existingSubscription = await _billingClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existingSubscription is not null)
        {
            EnsureSubscriptionBelongsToCustomer(existingSubscription, customer.Id);
            EnsureSubscriptionMatchesProduct(existingSubscription, product.Handle);
            await SaveSubscriptionMappingAsync(user.Id, product.Handle, subscriptionReference, existingSubscription, customer.Id);
            return ToSubscriptionDto(existingSubscription, product);
        }

        MaxioSubscription subscription;
        try
        {
            subscription = await _billingClient.CreateSubscriptionAsync(new CreateMaxioSubscription
            {
                ProductHandle = product.Handle,
                CustomerReference = customerReference,
                PaymentCollectionMethod = "remittance",
                Reference = subscriptionReference
            }, cancellationToken);
        }
        catch (MaxioApiException exception) when ((int)exception.StatusCode is >= 400 and < 500)
        {
            // Another request may have won the race between lookup and create. Re-read by
            // the same unique reference before surfacing a genuine validation failure.
            var winningSubscription = await _billingClient.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (winningSubscription is null)
            {
                throw;
            }

            subscription = winningSubscription;
        }

        EnsureSubscriptionBelongsToCustomer(subscription, customer.Id);
        EnsureSubscriptionMatchesProduct(subscription, product.Handle);
        await SaveSubscriptionMappingAsync(user.Id, product.Handle, subscriptionReference, subscription, customer.Id);
        return ToSubscriptionDto(subscription, product);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync()
    {
        var cancellationToken = RequestCancellationToken;
        var options = GetConfiguredOptions();
        var user = await GetCurrentUserAsync();
        var customerReference = CustomerReferencePrefix + user.Id;
        var customer = await _billingClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        await SaveCustomerMappingAsync(user.Id, customerReference, customer.Id);
        var products = await _billingClient.ListProductsAsync(options.ProductFamilyHandle!, cancellationToken);
        var productsByHandle = products.ToDictionary(product => product.Handle, StringComparer.OrdinalIgnoreCase);
        var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var result = new List<SubscriptionDto>(subscriptions.Count);

        foreach (var subscription in subscriptions)
        {
            var product = subscription.Product is null
                ? null
                : productsByHandle.GetValueOrDefault(subscription.Product.Handle);
            result.Add(ToSubscriptionDto(subscription, product));

            if (product is not null && !string.IsNullOrWhiteSpace(subscription.Reference))
            {
                await SaveSubscriptionMappingAsync(user.Id, product.Handle, subscription.Reference!, subscription, customer.Id);
            }
        }

        return result;
    }

    private MaxioOptions GetConfiguredOptions()
    {
        var options = _maxioOptions.Value;
        options.EnsureCredentials();
        return options;
    }

    private async Task<ApplicationUser> GetCurrentUserAsync()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var username = principal?.FindFirstValue(ClaimTypes.Name) ?? principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new UnauthorizedAccessException("The authenticated user identity is missing.");
        }

        var user = await _userManager.FindByNameAsync(username);
        return user ?? throw new UnauthorizedAccessException("The authenticated user no longer exists.");
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var customer = await _billingClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is not null)
        {
            await SaveCustomerMappingAsync(user.Id, reference, customer.Id);
            return customer;
        }

        var email = user.Email ?? user.UserName ?? reference;
        try
        {
            customer = await _billingClient.CreateCustomerAsync(new CreateMaxioCustomer
            {
                FirstName = "eShopOnWeb",
                LastName = email.Split('@')[0],
                Email = email,
                Reference = reference
            }, cancellationToken);
        }
        catch (MaxioApiException exception) when ((int)exception.StatusCode is >= 400 and < 500)
        {
            // Customer references are unique in Advanced Billing, so a concurrent create
            // is resolved by retrieving the winner rather than creating a second customer.
            var winningCustomer = await _billingClient.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (winningCustomer is null)
            {
                throw;
            }

            customer = winningCustomer;
        }

        await SaveCustomerMappingAsync(user.Id, reference, customer.Id);
        return customer;
    }

    private async Task SaveCustomerMappingAsync(string userId, string reference, long customerId)
    {
        try
        {
            var mapping = await _identityDbContext.MaxioCustomerMappings.FindAsync(new object[] { userId }, RequestCancellationToken);
            if (mapping is null)
            {
                _identityDbContext.MaxioCustomerMappings.Add(new MaxioCustomerMapping
                {
                    UserId = userId,
                    CustomerReference = reference,
                    MaxioCustomerId = customerId,
                    LastVerifiedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                mapping.CustomerReference = reference;
                mapping.MaxioCustomerId = customerId;
                mapping.LastVerifiedAt = DateTimeOffset.UtcNow;
            }

            await _identityDbContext.SaveChangesAsync(RequestCancellationToken);
        }
        catch (DbUpdateException exception)
        {
            foreach (var entry in exception.Entries)
            {
                entry.State = EntityState.Detached;
            }

            _logger.LogWarning(exception, "Could not persist the Maxio customer mapping for user {UserId}.", userId);
        }
    }

    private async Task SaveSubscriptionMappingAsync(string userId, string productHandle, string reference, MaxioSubscription subscription, long customerId)
    {
        try
        {
            var mapping = await _identityDbContext.MaxioSubscriptionMappings
                .SingleOrDefaultAsync(item => item.UserId == userId && item.ProductHandle == productHandle, RequestCancellationToken);
            if (mapping is null)
            {
                _identityDbContext.MaxioSubscriptionMappings.Add(new MaxioSubscriptionMapping
                {
                    UserId = userId,
                    ProductHandle = productHandle,
                    SubscriptionReference = reference,
                    MaxioSubscriptionId = subscription.Id,
                    MaxioCustomerId = customerId,
                    LastVerifiedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                mapping.SubscriptionReference = reference;
                mapping.MaxioSubscriptionId = subscription.Id;
                mapping.MaxioCustomerId = customerId;
                mapping.LastVerifiedAt = DateTimeOffset.UtcNow;
            }

            await _identityDbContext.SaveChangesAsync(RequestCancellationToken);
        }
        catch (DbUpdateException exception)
        {
            foreach (var entry in exception.Entries)
            {
                entry.State = EntityState.Detached;
            }

            _logger.LogWarning(exception, "Could not persist the Maxio subscription mapping for user {UserId} and plan {PlanHandle}.", userId, productHandle);
        }
    }

    private static void EnsureSubscriptionBelongsToCustomer(MaxioSubscription subscription, long customerId)
    {
        if (subscription.Customer?.Id is not null && subscription.Customer.Id != customerId)
        {
            throw new InvalidOperationException("The Maxio subscription reference belongs to another customer.");
        }
    }

    private static void EnsureSubscriptionMatchesProduct(MaxioSubscription subscription, string productHandle)
    {
        if (subscription.Product?.Handle is not null &&
            !string.Equals(subscription.Product.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Maxio subscription reference belongs to another plan.");
        }
    }

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription, MaxioProduct? fallbackProduct)
    {
        var priceInCents = subscription.ProductPriceInCents
            ?? subscription.CurrentBillingAmountInCents
            ?? subscription.Product?.PriceInCents
            ?? fallbackProduct?.PriceInCents
            ?? 0;
        var product = subscription.Product ?? fallbackProduct;

        return new SubscriptionDto
        {
            MaxioSubscriptionId = subscription.Id,
            Reference = subscription.Reference ?? string.Empty,
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            PriceInCents = priceInCents,
            Price = priceInCents / 100m,
            Currency = subscription.Currency,
            State = subscription.State,
            NextBillingDate = subscription.NextAssessmentAt
        };
    }

    private CancellationToken RequestCancellationToken => _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}
