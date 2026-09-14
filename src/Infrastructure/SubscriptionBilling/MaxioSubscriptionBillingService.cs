using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.SubscriptionBilling;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CustomerLocks = new(StringComparer.Ordinal);
    private static readonly object CatalogCacheKey = new();
    private static readonly TimeSpan CatalogCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PendingClaimDuration = TimeSpan.FromMinutes(2);

    private readonly IMaxioBillingClient _maxio;
    private readonly AppIdentityDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionBillingService(
        IMaxioBillingClient maxio,
        AppIdentityDbContext dbContext,
        IMemoryCache cache,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
        _productFamilyHandle = options.Value.ProductFamilyHandle;

        if (string.IsNullOrWhiteSpace(_productFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");
        }
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(cancellationToken);
        return catalog.Products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => new SubscriptionPlan(
                product.Handle!,
                product.Name,
                product.Description,
                product.PriceInCents,
                catalog.Currency,
                product.Interval,
                product.IntervalUnit))
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(
        SubscriptionUser user,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(cancellationToken);
        var product = catalog.Products.SingleOrDefault(candidate =>
            candidate.ArchivedAt is null &&
            string.Equals(candidate.Handle, productHandle?.Trim(), StringComparison.OrdinalIgnoreCase));

        if (product?.Handle is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle ?? string.Empty);
        }

        if (product.RequireCreditCard)
        {
            throw new SubscriptionPaymentMethodRequiredException(product.Handle);
        }

        var subscriptionReference = CreateReference("eshop-sub", $"{user.Id}|{product.Handle}");
        var gate = EnrollmentLocks.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            var existing = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                ValidateSubscriptionOwner(existing, user, product.Handle);
                await PersistSubscriptionAsync(user.Id, subscriptionReference, existing, catalog.Currency, null, cancellationToken);
                return new SubscriptionEnrollment(ToUserSubscription(existing, catalog.Currency), false);
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var claim = await ClaimCreationAsync(
                user.Id,
                product.Handle,
                subscriptionReference,
                customer.Id,
                cancellationToken);

            if (!claim.CanCreate)
            {
                var recovered = await WaitForSubscriptionAsync(subscriptionReference, cancellationToken);
                if (recovered is not null)
                {
                    ValidateSubscriptionOwner(recovered, user, product.Handle);
                    await PersistSubscriptionAsync(user.Id, subscriptionReference, recovered, catalog.Currency, claim.Mapping, cancellationToken);
                    return new SubscriptionEnrollment(ToUserSubscription(recovered, catalog.Currency), false);
                }

                throw new SubscriptionCreationInProgressException();
            }

            return await CreateSubscriptionAsync(
                user,
                product.Handle,
                subscriptionReference,
                customer.Id,
                catalog.Currency,
                catalog.PaymentCollectionMethod,
                claim.Mapping,
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<UserSubscription>> GetSubscriptionsAsync(
        SubscriptionUser user,
        CancellationToken cancellationToken = default)
    {
        var customerReference = CreateReference("eshop-user", user.Id);
        var customer = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<UserSubscription>();
        }

        await PersistCustomerAsync(user.Id, customerReference, customer.Id, cancellationToken);
        var catalog = await GetCatalogAsync(cancellationToken);
        var subscriptions = await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var matching = subscriptions
            .Where(subscription => subscription.Product is not null &&
                string.Equals(
                    subscription.Product.ProductFamily.Handle,
                    _productFamilyHandle,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(subscription => subscription.Id)
            .ToList();

        foreach (var subscription in matching)
        {
            var reference = CreateReference("eshop-sub", $"{user.Id}|{subscription.Product!.Handle}");
            await PersistSubscriptionAsync(user.Id, reference, subscription, catalog.Currency, null, cancellationToken);
        }

        return matching.Select(subscription => ToUserSubscription(subscription, catalog.Currency)).ToList();
    }

    private async Task<SubscriptionEnrollment> CreateSubscriptionAsync(
        SubscriptionUser user,
        string productHandle,
        string subscriptionReference,
        long customerId,
        string currency,
        string paymentCollectionMethod,
        MaxioSubscriptionMapping mapping,
        CancellationToken cancellationToken)
    {
        try
        {
            var subscription = await _maxio.CreateSubscriptionAsync(
                productHandle,
                customerId,
                subscriptionReference,
                mapping.UniquenessToken,
                paymentCollectionMethod,
                cancellationToken);
            ValidateSubscriptionOwner(subscription, user, productHandle);
            await PersistSubscriptionAsync(user.Id, subscriptionReference, subscription, currency, mapping, cancellationToken);
            return new SubscriptionEnrollment(ToUserSubscription(subscription, currency), true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is MaxioApiException or HttpRequestException or TaskCanceledException)
        {
            var recovered = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (recovered is not null)
            {
                ValidateSubscriptionOwner(recovered, user, productHandle);
                await PersistSubscriptionAsync(user.Id, subscriptionReference, recovered, currency, mapping, cancellationToken);
                return new SubscriptionEnrollment(ToUserSubscription(recovered, currency), false);
            }

            if (exception is MaxioApiException { StatusCode: (int)HttpStatusCode.UnprocessableEntity })
            {
                mapping.CreationStatus = SubscriptionCreationStatus.Failed;
                mapping.UpdatedAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                throw new SubscriptionBillingException(exception.Message, exception);
            }

            throw new SubscriptionBillingUnavailableException(
                "Maxio could not confirm the subscription. Retrying the same request is safe.",
                exception);
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriptionUser user, CancellationToken cancellationToken)
    {
        var customerReference = CreateReference("eshop-user", user.Id);
        var gate = CustomerLocks.GetOrAdd(customerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            var customer = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                var (firstName, lastName) = GetCustomerName(user);
                var uniquenessToken = CreateDeterministicGuid($"customer|{user.Id}").ToString();

                try
                {
                    customer = await _maxio.CreateCustomerAsync(
                        firstName,
                        lastName,
                        user.Email,
                        customerReference,
                        uniquenessToken,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is MaxioApiException or HttpRequestException or TaskCanceledException)
                {
                    customer = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
                    if (customer is null)
                    {
                        throw new SubscriptionBillingUnavailableException(
                            "Maxio could not confirm the customer record. Retrying the same request is safe.",
                            exception);
                    }
                }
            }

            await PersistCustomerAsync(user.Id, customerReference, customer.Id, cancellationToken);
            return customer;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CreationClaim> ClaimCreationAsync(
        string userId,
        string productHandle,
        string subscriptionReference,
        long customerId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var mapping = await _dbContext.MaxioSubscriptionMappings.SingleOrDefaultAsync(
            item => item.UserId == userId && item.ProductHandle == productHandle,
            cancellationToken);

        if (mapping is null)
        {
            mapping = new MaxioSubscriptionMapping
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ProductHandle = productHandle,
                SubscriptionReference = subscriptionReference,
                UniquenessToken = Guid.NewGuid().ToString(),
                CreationStatus = SubscriptionCreationStatus.Pending,
                MaxioCustomerId = customerId,
                UpdatedAt = now
            };
            _dbContext.MaxioSubscriptionMappings.Add(mapping);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new CreationClaim(mapping, true);
            }
            catch (DbUpdateException)
            {
                _dbContext.ChangeTracker.Clear();
                mapping = await _dbContext.MaxioSubscriptionMappings.SingleAsync(
                    item => item.UserId == userId && item.ProductHandle == productHandle,
                    cancellationToken);
                return new CreationClaim(mapping, false);
            }
        }

        if (mapping.CreationStatus == SubscriptionCreationStatus.Completed)
        {
            throw new SubscriptionBillingUnavailableException(
                "The local subscription record exists but Maxio did not return it. No new subscription was created.");
        }

        if (mapping.CreationStatus == SubscriptionCreationStatus.Pending &&
            now - mapping.UpdatedAt < PendingClaimDuration)
        {
            return new CreationClaim(mapping, false);
        }

        if (mapping.CreationStatus == SubscriptionCreationStatus.Failed)
        {
            mapping.UniquenessToken = Guid.NewGuid().ToString();
        }

        mapping.CreationStatus = SubscriptionCreationStatus.Pending;
        mapping.MaxioCustomerId = customerId;
        mapping.UpdatedAt = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new CreationClaim(mapping, true);
    }

    private async Task PersistCustomerAsync(
        string userId,
        string customerReference,
        long customerId,
        CancellationToken cancellationToken)
    {
        var mapping = await _dbContext.MaxioCustomerMappings.FindAsync(new object[] { userId }, cancellationToken);
        if (mapping is null)
        {
            mapping = new MaxioCustomerMapping { UserId = userId };
            _dbContext.MaxioCustomerMappings.Add(mapping);
        }

        mapping.CustomerReference = customerReference;
        mapping.MaxioCustomerId = customerId;
        mapping.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            _logger.LogWarning(exception, "Could not persist the Maxio customer mapping for user {UserId}.", userId);
            _dbContext.ChangeTracker.Clear();
        }
    }

    private async Task PersistSubscriptionAsync(
        string userId,
        string subscriptionReference,
        MaxioSubscription subscription,
        string currency,
        MaxioSubscriptionMapping? mapping,
        CancellationToken cancellationToken)
    {
        var productHandle = subscription.Product?.Handle
            ?? throw new SubscriptionBillingUnavailableException("Maxio returned a subscription without a product handle.");

        mapping ??= await _dbContext.MaxioSubscriptionMappings.SingleOrDefaultAsync(
            item => item.UserId == userId && item.ProductHandle == productHandle,
            cancellationToken);

        if (mapping is null)
        {
            mapping = new MaxioSubscriptionMapping
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ProductHandle = productHandle,
                SubscriptionReference = subscriptionReference,
                UniquenessToken = Guid.NewGuid().ToString()
            };
            _dbContext.MaxioSubscriptionMappings.Add(mapping);
        }

        mapping.CreationStatus = SubscriptionCreationStatus.Completed;
        mapping.MaxioCustomerId = subscription.Customer.Id;
        mapping.MaxioSubscriptionId = subscription.Id;
        mapping.State = subscription.State;
        mapping.PriceInCents = subscription.ProductPriceInCents;
        mapping.Currency = currency;
        mapping.NextBillingDate = subscription.CurrentPeriodEndsAt;
        mapping.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            _logger.LogWarning(exception, "Could not persist Maxio subscription {SubscriptionId}.", subscription.Id);
            _dbContext.ChangeTracker.Clear();
        }
    }

    private async Task<CatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var cached = await _cache.GetOrCreateAsync(CatalogCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CatalogCacheDuration;
            var site = await _maxio.GetSiteAsync(cancellationToken);
            var products = await _maxio.GetProductsAsync(_productFamilyHandle, cancellationToken);
            var paymentCollectionMethod = site.RelationshipInvoicingEnabled ? "remittance" : "invoice";
            return new CatalogSnapshot(site.Currency, paymentCollectionMethod, products);
        });

        return cached ?? throw new SubscriptionBillingUnavailableException("Maxio returned no subscription catalog.");
    }

    private async Task<MaxioSubscription?> WaitForSubscriptionAsync(
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            var subscription = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (subscription is not null)
            {
                return subscription;
            }
        }

        return null;
    }

    private async Task<MaxioSubscription?> TryFindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _maxio.FindSubscriptionAsync(reference, cancellationToken);
        }
        catch (Exception exception) when (exception is MaxioApiException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Could not reconcile Maxio subscription reference {Reference}.", reference);
            return null;
        }
    }

    private void ValidateSubscriptionOwner(MaxioSubscription subscription, SubscriptionUser user, string productHandle)
    {
        var expectedCustomerReference = CreateReference("eshop-user", user.Id);
        if (!string.Equals(subscription.Customer.Reference, expectedCustomerReference, StringComparison.Ordinal) ||
            !string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionBillingUnavailableException(
                "Maxio returned a subscription that does not match the authenticated user and requested plan.");
        }
    }

    private static UserSubscription ToUserSubscription(MaxioSubscription subscription, string currency)
    {
        var product = subscription.Product
            ?? throw new SubscriptionBillingUnavailableException("Maxio returned a subscription without product details.");
        return new UserSubscription(
            subscription.Id,
            product.Handle ?? string.Empty,
            product.Name,
            subscription.ProductPriceInCents,
            currency,
            product.Interval,
            product.IntervalUnit,
            subscription.State,
            subscription.CurrentPeriodEndsAt);
    }

    private static (string FirstName, string LastName) GetCustomerName(SubscriptionUser user)
    {
        var localPart = user.Email.Split('@', 2)[0];
        var pieces = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = pieces.FirstOrDefault() ?? "eShop";
        var lastName = pieces.Skip(1).FirstOrDefault() ?? "Customer";
        return (Truncate(firstName, 100), Truncate(lastName, 100));
    }

    private static string CreateReference(string prefix, string value)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        return $"{prefix}-{hash}";
    }

    private static Guid CreateDeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record CatalogSnapshot(
        string Currency,
        string PaymentCollectionMethod,
        IReadOnlyList<MaxioProduct> Products);
    private sealed record CreationClaim(MaxioSubscriptionMapping Mapping, bool CanCreate);
}
