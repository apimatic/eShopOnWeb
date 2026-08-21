using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<CreateSubscriptionResponse> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}

internal sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private const string CustomerReferencePrefix = "eshop-user:";
    private readonly IMaxioClient _maxioClient;
    private readonly IShopperIdentityService _shopperIdentityService;
    private readonly MaxioOptions _options;
    private readonly AsyncKeyedLocker _keyedLocker;

    public SubscriptionBillingService(
        IMaxioClient maxioClient,
        IShopperIdentityService shopperIdentityService,
        IOptions<MaxioOptions> options,
        AsyncKeyedLocker keyedLocker)
    {
        _maxioClient = maxioClient;
        _shopperIdentityService = shopperIdentityService;
        _options = options.Value;
        _keyedLocker = keyedLocker;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var siteTask = _maxioClient.GetSiteAsync(cancellationToken);
        var productsTask = _maxioClient.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        await Task.WhenAll(siteTask, productsTask);

        var site = await siteTask;
        var products = await productsTask;

        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .OrderBy(product => product.PriceInCents)
            .Select(product => MapPlan(product, site.Currency))
            .ToList();
    }

    public async Task<CreateSubscriptionResponse> SubscribeAsync(
        string userName,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(userName);
        var normalizedHandle = productHandle.Trim();
        var reference = SubscriptionReference(user.Id, normalizedHandle);

        using (await _keyedLocker.LockAsync(reference, cancellationToken))
        {
            var products = await _maxioClient.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
            var product = products.SingleOrDefault(item =>
                item.ArchivedAt is null && string.Equals(item.Handle, normalizedHandle, StringComparison.Ordinal));

            if (product is null)
            {
                throw new SubscriptionPlanNotFoundException(normalizedHandle);
            }

            if (product.RequireCreditCard)
            {
                throw new PaymentMethodRequiredException(normalizedHandle);
            }

            var existing = await _maxioClient.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return new CreateSubscriptionResponse
                {
                    Subscription = MapSubscription(existing),
                    AlreadyExisted = true
                };
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var createRequest = new MaxioCreateSubscription
            {
                ProductHandle = normalizedHandle,
                CustomerId = customer.Id,
                Reference = reference,
                PaymentCollectionMethod = "remittance"
            };

            MaxioSubscription subscription;
            try
            {
                subscription = await _maxioClient.CreateSubscriptionAsync(createRequest, cancellationToken);
            }
            catch (MaxioApiException exception) when (
                exception.StatusCode == HttpStatusCode.UnprocessableEntity || (int)exception.StatusCode >= 500)
            {
                subscription = await RecoverSubscriptionAsync(reference, exception, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                subscription = await RecoverSubscriptionAsync(reference, exception, cancellationToken);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                subscription = await RecoverSubscriptionAsync(reference, exception, cancellationToken);
            }

            return new CreateSubscriptionResponse
            {
                Subscription = MapSubscription(subscription),
                AlreadyExisted = false
            };
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        string userName,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(userName);
        var customer = await _maxioClient.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(subscription => string.Equals(
                subscription.Product.ProductFamily.Handle,
                _options.ProductFamilyHandle,
                StringComparison.Ordinal))
            .OrderByDescending(subscription => subscription.CreatedAt)
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<ShopperIdentity> FindUserAsync(string userName)
    {
        var user = await _shopperIdentityService.FindByNameAsync(userName);
        return user ?? throw new ShopperNotFoundException();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        ShopperIdentity user,
        CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var existing = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email;
        var at = email.IndexOf('@');
        var firstName = at > 0 ? email[..at] : email;
        var customer = new MaxioCreateCustomer
        {
            FirstName = firstName,
            LastName = "eShopOnWeb",
            Email = email,
            Reference = reference
        };

        try
        {
            return await _maxioClient.CreateCustomerAsync(customer, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var racedCustomer = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (racedCustomer is not null)
            {
                return racedCustomer;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription> RecoverSubscriptionAsync(
        string reference,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var subscription = await _maxioClient.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (subscription is not null)
            {
                return subscription;
            }

            if (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
            }
        }

        throw originalException;
    }

    private static string CustomerReference(string userId) => $"{CustomerReferencePrefix}{userId}";

    private static string SubscriptionReference(string userId, string productHandle) =>
        $"{CustomerReference(userId)}:product:{productHandle}";

    private static SubscriptionPlanDto MapPlan(MaxioProduct product, string currency) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard,
        CanSubscribe = !product.RequireCreditCard
    };

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.Product.Handle ?? string.Empty,
        ProductName = subscription.Product.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Product.Interval,
        IntervalUnit = subscription.Product.IntervalUnit,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };
}
