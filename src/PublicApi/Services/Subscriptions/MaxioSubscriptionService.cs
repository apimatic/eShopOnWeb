using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Services.Maxio;
using Microsoft.eShopWeb.PublicApi.Services.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Services.Subscriptions;

public class MaxioSubscriptionService : ISubscriptionService
{
    private const string RemittanceCollectionMethod = "remittance";

    private readonly IMaxioApi _maxioApi;

    public MaxioSubscriptionService(IMaxioApi maxioApi)
    {
        _maxioApi = maxioApi;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxioApi.ListFamilyProductsAsync(cancellationToken);

        return products
            .Where(product => product.ArchivedAt is null &&
                              !string.IsNullOrWhiteSpace(product.Handle) &&
                              !string.IsNullOrWhiteSpace(product.Name))
            .OrderBy(product => product.PriceInCents)
            .Select(product => new SubscriptionPlan(
                product.Id,
                product.Handle!,
                product.Name!,
                product.Description,
                product.PriceInCents,
                ToDollars(product.PriceInCents),
                product.Interval,
                product.IntervalUnit))
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        string userName,
        string planHandle,
        string firstName,
        string lastName,
        string? email,
        CancellationToken cancellationToken)
    {
        var product = await FindPurchasableProductAsync(planHandle, cancellationToken);
        if (product is null)
        {
            throw new UnknownPlanException(planHandle);
        }

        string customerReference = BuildCustomerReference(userName);
        var customer = await EnsureCustomerAsync(customerReference, firstName, lastName, email, userName, cancellationToken);

        string subscriptionReference = BuildSubscriptionReference(userName, product.Handle!);

        var existing = await _maxioApi.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return new SubscribeResult(ToCurrentSubscription(existing), AlreadySubscribed: true);
        }

        try
        {
            var created = await _maxioApi.CreateSubscriptionAsync(new MaxioSubscriptionDraft
            {
                ProductHandle = product.Handle,
                CustomerReference = customer.Reference ?? customerReference,
                Reference = subscriptionReference,
                PaymentCollectionMethod = RemittanceCollectionMethod
            }, cancellationToken);

            return new SubscribeResult(ToCurrentSubscription(created), AlreadySubscribed: false);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent subscribe for the same user and plan may have already created this
            // subscription (Maxio enforces a unique subscription reference). Treat it as idempotent.
            var concurrent = await _maxioApi.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (concurrent is not null)
            {
                return new SubscribeResult(ToCurrentSubscription(concurrent), AlreadySubscribed: true);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<CurrentSubscription>> ListSubscriptionsForUserAsync(
        string userName,
        CancellationToken cancellationToken)
    {
        string customerReference = BuildCustomerReference(userName);
        var customer = await _maxioApi.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return new List<CurrentSubscription>();
        }

        var subscriptions = await _maxioApi.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(subscription => subscription.CreatedAt)
            .Select(ToCurrentSubscription)
            .ToList();
    }

    private async Task<MaxioProduct?> FindPurchasableProductAsync(string planHandle, CancellationToken cancellationToken)
    {
        var products = await _maxioApi.ListFamilyProductsAsync(cancellationToken);
        return products.FirstOrDefault(product =>
            product.ArchivedAt is null &&
            string.Equals(product.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        string customerReference,
        string firstName,
        string lastName,
        string? email,
        string userName,
        CancellationToken cancellationToken)
    {
        var customer = await _maxioApi.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        try
        {
            return await _maxioApi.CreateCustomerAsync(new MaxioCustomerDraft
            {
                FirstName = firstName,
                LastName = lastName,
                Email = string.IsNullOrWhiteSpace(email) ? userName : email,
                Reference = customerReference
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request may have created the customer first (reference is unique in Maxio).
            var concurrent = await _maxioApi.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (concurrent is not null)
            {
                return concurrent;
            }

            throw;
        }
    }

    private static string BuildCustomerReference(string userName) => userName.Trim();

    private static string BuildSubscriptionReference(string userName, string planHandle)
        => $"{userName.Trim()}:{planHandle}";

    private static CurrentSubscription ToCurrentSubscription(MaxioSubscription subscription)
    {
        var product = subscription.Product;
        return new CurrentSubscription(
            subscription.Id,
            subscription.State ?? "unknown",
            product?.Handle ?? string.Empty,
            product?.Name ?? string.Empty,
            subscription.ProductPriceInCents ?? product?.PriceInCents ?? 0,
            subscription.CurrentPeriodEndsAt,
            subscription.CreatedAt);
    }

    private static decimal ToDollars(long cents) => cents / 100m;
}
