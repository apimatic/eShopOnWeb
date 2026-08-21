using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppIdentityDbContext _identityDbContext;
    private readonly SubscriptionOperationCoordinator _coordinator;
    private readonly MaxioOptions _options;

    public SubscriptionService(
        IMaxioClient maxioClient,
        UserManager<ApplicationUser> userManager,
        AppIdentityDbContext identityDbContext,
        SubscriptionOperationCoordinator coordinator,
        IOptions<MaxioOptions> options)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _identityDbContext = identityDbContext;
        _coordinator = coordinator;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var siteTask = _maxioClient.ReadSiteAsync(cancellationToken);
        var productsTask = _maxioClient.ListProductsForFamilyAsync(
            _options.ProductFamilyHandle,
            cancellationToken);
        await Task.WhenAll(siteTask, productsTask);
        var site = await siteTask;
        var products = await productsTask;

        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .OrderBy(product => product.PriceInCents)
            .ThenBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
            .Select(product => MapPlan(product, site.Currency))
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        string userName,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionRequestException("productHandle is required.");
        }

        var user = await FindUserAsync(userName);
        productHandle = productHandle.Trim();

        using var operation = await _coordinator.AcquireAsync(
            $"{user.Id}:{productHandle}",
            cancellationToken);

        var plans = await _maxioClient.ListProductsForFamilyAsync(
            _options.ProductFamilyHandle,
            cancellationToken);
        var plan = plans.SingleOrDefault(product =>
            product.ArchivedAt is null &&
            string.Equals(product.Handle, productHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var customerReference = CustomerReference(user.Id);
        var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
        var site = await _maxioClient.ReadSiteAsync(cancellationToken);
        var subscriptionReference = SubscriptionReference(user.Id, productHandle);
        var subscription = await _maxioClient.FindSubscriptionByReferenceAsync(
            subscriptionReference,
            cancellationToken);

        if (subscription is null)
        {
            try
            {
                subscription = await _maxioClient.CreateSubscriptionAsync(
                    new CreateMaxioSubscription(
                        productHandle,
                        customer.Id,
                        subscriptionReference,
                        site.RelationshipInvoicingEnabled ? "remittance" : "invoice"),
                    cancellationToken);
            }
            catch (MaxioApiException)
            {
                // A timeout or duplicate-reference response may follow a successful remote create.
                subscription = await _maxioClient.FindSubscriptionByReferenceAsync(
                    subscriptionReference,
                    cancellationToken);
                if (subscription is null)
                {
                    throw;
                }
            }
        }

        EnsureReferenceOwnership(subscription, customer.Id, productHandle);
        await UpsertSubscriptionLinkAsync(user.Id, subscription, subscriptionReference, cancellationToken);
        return MapSubscription(subscription);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(
        string userName,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(userName);
        var customerReference = CustomerReference(user.Id);
        var customer = await _maxioClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        await UpsertCustomerLinkAsync(user.Id, customer, customerReference, cancellationToken);
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var ownedSubscriptions = subscriptions
            .Where(subscription => string.Equals(
                subscription.Product.ProductFamilyHandle,
                _options.ProductFamilyHandle,
                StringComparison.Ordinal))
            .ToList();

        foreach (var subscription in ownedSubscriptions.Where(subscription => !string.IsNullOrWhiteSpace(subscription.Product.Handle)))
        {
            await UpsertSubscriptionLinkAsync(
                user.Id,
                subscription,
                subscription.Reference ?? SubscriptionReference(user.Id, subscription.Product.Handle),
                cancellationToken,
                saveChanges: false);
        }

        await _identityDbContext.SaveChangesAsync(cancellationToken);
        return ownedSubscriptions.Select(MapSubscription).ToList();
    }

    private async Task<ApplicationUser> FindUserAsync(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new SubscriptionIdentityException();
        }

        return await _userManager.FindByNameAsync(userName) ?? throw new SubscriptionIdentityException();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        ApplicationUser user,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            var email = user.Email ?? user.UserName ?? throw new SubscriptionIdentityException();
            var firstName = email.Split('@', 2)[0];
            try
            {
                customer = await _maxioClient.CreateCustomerAsync(
                    new CreateMaxioCustomer(firstName, "eShopOnWeb", email, customerReference),
                    cancellationToken);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                customer = await _maxioClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
                if (customer is null)
                {
                    throw;
                }
            }
        }

        await UpsertCustomerLinkAsync(user.Id, customer, customerReference, cancellationToken);
        return customer;
    }

    private async Task UpsertCustomerLinkAsync(
        string userId,
        MaxioCustomer customer,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var link = await _identityDbContext.MaxioCustomerLinks.FindAsync(new object[] { userId }, cancellationToken);
        if (link is null)
        {
            _identityDbContext.MaxioCustomerLinks.Add(new MaxioCustomerLink
            {
                UserId = userId,
                MaxioCustomerId = customer.Id,
                CustomerReference = customerReference,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            link.MaxioCustomerId = customer.Id;
            link.CustomerReference = customerReference;
            link.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _identityDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertSubscriptionLinkAsync(
        string userId,
        MaxioSubscription subscription,
        string subscriptionReference,
        CancellationToken cancellationToken,
        bool saveChanges = true)
    {
        var productHandle = subscription.Product.Handle;
        var link = await _identityDbContext.MaxioSubscriptionLinks.FindAsync(
            new object[] { userId, productHandle },
            cancellationToken);
        if (link is null)
        {
            _identityDbContext.MaxioSubscriptionLinks.Add(new MaxioSubscriptionLink
            {
                UserId = userId,
                ProductHandle = productHandle,
                MaxioSubscriptionId = subscription.Id,
                SubscriptionReference = subscriptionReference,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            link.MaxioSubscriptionId = subscription.Id;
            link.SubscriptionReference = subscriptionReference;
            link.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (saveChanges)
        {
            await _identityDbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static void EnsureReferenceOwnership(MaxioSubscription subscription, int customerId, string productHandle)
    {
        if (subscription.Customer.Id != customerId ||
            !string.Equals(subscription.Product.Handle, productHandle, StringComparison.Ordinal))
        {
            throw new SubscriptionReferenceConflictException();
        }
    }

    private static string CustomerReference(string userId) => $"eshop-user-{userId}";

    private static string SubscriptionReference(string userId, string productHandle) =>
        $"eshop-subscription-{userId}-{productHandle}";

    private static SubscriptionPlanDto MapPlan(MaxioProduct product, string currency) => new(
        product.Id,
        product.Handle,
        product.Name,
        product.Description,
        product.PriceInCents,
        product.PriceInCents / 100m,
        currency,
        product.Interval,
        product.IntervalUnit,
        product.RequireCreditCard);

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.Product.Handle,
        subscription.Product.Name,
        subscription.ProductPriceInCents,
        subscription.ProductPriceInCents / 100m,
        subscription.Currency,
        subscription.Product.Interval,
        subscription.Product.IntervalUnit,
        subscription.State,
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
}
