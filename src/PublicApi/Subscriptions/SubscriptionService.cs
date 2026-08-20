using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly IMaxioBillingClient _maxio;
    private readonly ISubscriptionMappingStore _mappingStore;
    private readonly ISubscriptionOperationLock _operationLock;

    public SubscriptionService(IMaxioBillingClient maxio, ISubscriptionMappingStore mappingStore,
        ISubscriptionOperationLock operationLock)
    {
        _maxio = maxio;
        _mappingStore = mappingStore;
        _operationLock = operationLock;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle))
            .OrderBy(product => product.PriceInCents)
            .Select(ToPlanDto)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(ApplicationUser user, string productHandle,
        CancellationToken cancellationToken)
    {
        var normalizedHandle = productHandle.Trim();
        await using var operationLock = await _operationLock.AcquireAsync(
            $"{user.Id}:{normalizedHandle.ToUpperInvariant()}", cancellationToken);
        var products = await _maxio.ListProductsAsync(cancellationToken);
        var product = products.SingleOrDefault(candidate =>
            string.Equals(candidate.Handle, normalizedHandle, StringComparison.OrdinalIgnoreCase));
        if (product?.Handle == null)
        {
            throw new SubscriptionPlanNotFoundException(normalizedHandle);
        }

        var customerReference = CustomerReference(user.Id);
        var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
        var subscriptions = await _maxio.ListSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = subscriptions.FirstOrDefault(subscription =>
            string.Equals(subscription.Product?.Handle, product.Handle, StringComparison.OrdinalIgnoreCase)
            && !TerminalStates.Contains(subscription.State));

        if (existing != null)
        {
            await _mappingStore.SyncAsync(user.Id, customer, subscriptions, cancellationToken);
            return new SubscribeResult { Subscription = ToSubscriptionDto(existing), AlreadySubscribed = true };
        }

        var latestTerminalId = subscriptions
            .Where(subscription => string.Equals(subscription.Product?.Handle, product.Handle,
                StringComparison.OrdinalIgnoreCase))
            .Select(subscription => subscription.Id)
            .DefaultIfEmpty(0)
            .Max();
        var operation = $"subscription:{user.Id}:{product.Handle}:{latestTerminalId}";

        MaxioSubscription created;
        try
        {
            created = await _maxio.CreateSubscriptionAsync(customerReference, product.Handle,
                SubscriptionReference(user.Id, product.Handle), UniquenessToken(operation), cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            subscriptions = await _maxio.ListSubscriptionsAsync(customer.Id, cancellationToken);
            var recovered = subscriptions.FirstOrDefault(subscription =>
                string.Equals(subscription.Product?.Handle, product.Handle, StringComparison.OrdinalIgnoreCase)
                && !TerminalStates.Contains(subscription.State));
            if (recovered == null)
            {
                throw;
            }

            created = recovered;
        }

        var synchronized = subscriptions.Concat(new[] { created })
            .GroupBy(subscription => subscription.Id)
            .Select(group => group.First())
            .ToList();
        await _mappingStore.SyncAsync(user.Id, customer, synchronized, cancellationToken);

        return new SubscribeResult { Subscription = ToSubscriptionDto(created), AlreadySubscribed = false };
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var customerReference = CustomerReference(user.Id);
        var customer = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListSubscriptionsAsync(customer.Id, cancellationToken);
        await _mappingStore.SyncAsync(user.Id, customer, subscriptions, cancellationToken);
        return subscriptions.OrderByDescending(subscription => subscription.Id).Select(ToSubscriptionDto).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, string reference,
        CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerAsync(reference, cancellationToken);
        if (customer != null)
        {
            return customer;
        }

        var email = user.Email ?? user.UserName
            ?? throw new InvalidOperationException("The authenticated user does not have an email address.");
        var firstName = email.Split('@', 2)[0];

        try
        {
            return await _maxio.CreateCustomerAsync(reference, firstName, "Customer", email,
                UniquenessToken($"customer:{user.Id}"), cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode is HttpStatusCode.Conflict
                                                   or HttpStatusCode.UnprocessableEntity)
        {
            var recovered = await _maxio.FindCustomerAsync(reference, cancellationToken);
            if (recovered == null)
            {
                throw;
            }

            return recovered;
        }
    }

    private static string CustomerReference(string userId) => $"eshop:{userId}";

    private static string SubscriptionReference(string userId, string productHandle) =>
        $"eshop-{UniquenessToken($"reference:{userId}:{productHandle}")}";

    private static string UniquenessToken(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        PricePointName = product.PricePointName,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
    };
}
