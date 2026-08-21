using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioClient _maxio;
    private readonly MaxioOptions _options;
    private readonly AsyncKeyedLock _keyedLock;
    private readonly IMemoryCache _cache;

    public SubscriptionService(
        IMaxioClient maxio,
        IOptions<MaxioOptions> options,
        AsyncKeyedLock keyedLock,
        IMemoryCache cache)
    {
        _maxio = maxio;
        _options = options.Value;
        _keyedLock = keyedLock;
        _cache = cache;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .OrderBy(product => product.PriceInCents)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResponse?> SubscribeAsync(
        SubscriptionUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        productHandle = productHandle.Trim();
        if (productHandle.Length == 0)
        {
            return null;
        }

        var plan = (await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken))
            .SingleOrDefault(product =>
                product.ArchivedAt is null &&
                string.Equals(product.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan?.Handle is null)
        {
            return null;
        }

        var subscriptionReference = BuildReference("eshop-sub", $"{user.Id}\n{plan.Handle}");
        using var keyedLock = await _keyedLock.AcquireAsync(subscriptionReference, cancellationToken);

        if (_cache.TryGetValue<MaxioSubscription>(subscriptionReference, out var cached) && cached is not null)
        {
            return new SubscribeResponse(false, MapSubscription(cached));
        }

        var existing = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            EnsureSubscriptionOwnership(existing, user, plan.Handle);
            Cache(subscriptionReference, existing);
            return new SubscribeResponse(false, MapSubscription(existing));
        }

        var customerReference = BuildReference("eshop-user", user.Id);
        var customer = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
        var signup = BuildSignup(user, plan.Handle, subscriptionReference, customerReference, customer);

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(signup, cancellationToken);
            EnsureSubscriptionOwnership(created, user, plan.Handle);
            Cache(subscriptionReference, created);
            return new SubscribeResponse(true, MapSubscription(created));
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A request in another app instance may have won the race. The deterministic
            // reference lets us recover the completed Maxio resource without creating again.
            existing = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                EnsureSubscriptionOwnership(existing, user, plan.Handle);
                Cache(subscriptionReference, existing);
                return new SubscribeResponse(false, MapSubscription(existing));
            }

            // Different products can be submitted concurrently for a new shopper. If
            // customer_attributes lost the unique-reference race, resolve that customer
            // and retry this subscription with customer_id.
            if (signup.CustomerAttributes is not null)
            {
                customer = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
                if (customer is not null)
                {
                    signup.CustomerAttributes = null;
                    signup.CustomerId = customer.Id;
                    var created = await _maxio.CreateSubscriptionAsync(signup, cancellationToken);
                    EnsureSubscriptionOwnership(created, user, plan.Handle);
                    Cache(subscriptionReference, created);
                    return new SubscribeResponse(true, MapSubscription(created));
                }
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(
        SubscriptionUser user,
        CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerAsync(BuildReference("eshop-user", user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(subscription => string.Equals(
                subscription.Product.ProductFamily.Handle,
                _options.ProductFamilyHandle,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(subscription => subscription.Id)
            .Select(MapSubscription)
            .ToList();
    }

    private static MaxioCreateSubscription BuildSignup(
        SubscriptionUser user,
        string productHandle,
        string subscriptionReference,
        string customerReference,
        MaxioCustomer? customer)
    {
        var signup = new MaxioCreateSubscription
        {
            ProductHandle = productHandle,
            Reference = subscriptionReference,
            CustomerId = customer?.Id
        };

        if (customer is null)
        {
            var (firstName, lastName) = GetNames(user.UserName);
            signup.CustomerAttributes = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = user.Email,
                Reference = customerReference
            };
        }

        return signup;
    }

    private void EnsureSubscriptionOwnership(
        MaxioSubscription subscription,
        SubscriptionUser user,
        string productHandle)
    {
        var expectedCustomerReference = BuildReference("eshop-user", user.Id);
        if (!string.Equals(subscription.Customer.Reference, expectedCustomerReference, StringComparison.Ordinal) ||
            !string.Equals(subscription.Product.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The billing reference resolved to an unexpected subscription.");
        }
    }

    private void Cache(string reference, MaxioSubscription subscription) =>
        _cache.Set(reference, subscription, TimeSpan.FromMinutes(5));

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) => new(
        product.Id,
        product.Handle!,
        product.Name,
        product.Description,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit,
        product.RequireCreditCard);

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.Product.Handle ?? string.Empty,
        subscription.Product.Name,
        subscription.ProductPriceInCents,
        subscription.State,
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        subscription.CurrentPeriodEndsAt);

    private static string BuildReference(string prefix, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"{prefix}-{Convert.ToHexString(hash).ToLowerInvariant()[..32]}";
    }

    private static (string FirstName, string LastName) GetNames(string userName)
    {
        var localPart = userName.Split('@', 2)[0];
        var names = localPart.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return names.Length switch
        {
            0 => ("eShop", "Customer"),
            1 => (names[0], "Customer"),
            _ => (names[0], names[^1])
        };
    }
}
