using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public sealed class SubscriptionService : ISubscriptionService
{
    private readonly ISubscriptionBillingGateway _billing;
    private readonly IRepository<SubscriptionLink> _links;
    private readonly ISubscriptionOperationLock _operationLock;

    public SubscriptionService(
        ISubscriptionBillingGateway billing,
        IRepository<SubscriptionLink> links,
        ISubscriptionOperationLock operationLock)
    {
        _billing = billing;
        _links = links;
        _operationLock = operationLock;
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
        _billing.ListPlansAsync(cancellationToken);

    public async Task<BillingSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingPlanNotFoundException(productHandle ?? string.Empty);
        }

        productHandle = productHandle.Trim();
        await using var operation = await _operationLock.AcquireAsync(
            $"{shopper.UserId}\n{productHandle}", cancellationToken);

        var plans = await _billing.ListPlansAsync(cancellationToken);
        if (!plans.Any(x => string.Equals(x.Handle, productHandle, StringComparison.Ordinal)))
        {
            throw new BillingPlanNotFoundException(productHandle);
        }

        var customerReference = BuildReference("customer", shopper.UserId);
        var customer = await _billing.FindCustomerAsync(customerReference, cancellationToken)
            ?? await CreateCustomerAsync(shopper, customerReference, cancellationToken);

        var subscriptionReference = BuildReference("subscription", shopper.UserId, productHandle);
        var subscriptions = await _billing.ListSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = subscriptions.FirstOrDefault(x =>
            string.Equals(x.Reference, subscriptionReference, StringComparison.Ordinal) ||
            string.Equals(x.ProductHandle, productHandle, StringComparison.Ordinal));

        if (existing is not null)
        {
            await SynchronizeLinkAsync(shopper.UserId, productHandle, subscriptionReference, customer.Id, existing.Id, cancellationToken);
            return existing;
        }

        var created = await _billing.CreateSubscriptionAsync(
            customer.Id, productHandle, subscriptionReference, cancellationToken);
        await SynchronizeLinkAsync(shopper.UserId, productHandle, subscriptionReference, customer.Id, created.Id, cancellationToken);
        return created;
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListForShopperAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken)
    {
        var customer = await _billing.FindCustomerAsync(
            BuildReference("customer", shopper.UserId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return await _billing.ListSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<BillingCustomer> CreateCustomerAsync(
        ShopperIdentity shopper,
        string reference,
        CancellationToken cancellationToken)
    {
        var (firstName, lastName) = NamesFromEmail(shopper.Email);
        try
        {
            return await _billing.CreateCustomerAsync(
                new NewBillingCustomer(firstName, lastName, shopper.Email, reference),
                cancellationToken);
        }
        catch (BillingProviderException ex) when (ex.StatusCode == 422)
        {
            // Customer references are unique in Maxio. A concurrent request may have won the create race.
            var existing = await _billing.FindCustomerAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private async Task SynchronizeLinkAsync(
        string userId,
        string productHandle,
        string subscriptionReference,
        long customerId,
        long subscriptionId,
        CancellationToken cancellationToken)
    {
        var specification = new SubscriptionLinkByUserAndProductSpec(userId, productHandle);
        var link = await _links.FirstOrDefaultAsync(specification, cancellationToken);
        if (link is null)
        {
            link = new SubscriptionLink(userId, productHandle, subscriptionReference);
            link.Synchronize(customerId, subscriptionId);
            await _links.AddAsync(link, cancellationToken);
        }
        else
        {
            link.Synchronize(customerId, subscriptionId);
            await _links.UpdateAsync(link, cancellationToken);
        }
    }

    private static string BuildReference(string kind, params string[] values)
    {
        var input = string.Join('\n', values);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
        return $"eshop-{kind}-{digest[..32]}";
    }

    private static (string FirstName, string LastName) NamesFromEmail(string email)
    {
        var localPart = email.Split('@', 2)[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return ("eShop", "Customer");
        }

        static string ToName(string value) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
        var firstName = ToName(parts[0]);
        var lastName = parts.Length > 1 ? string.Join(' ', parts.Skip(1).Select(ToName)) : "Customer";
        return (firstName, lastName);
    }
}
