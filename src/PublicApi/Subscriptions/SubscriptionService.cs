using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OperationLocks = new();
    private readonly IMaxioClient _maxio;
    private readonly MaxioOptions _options;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CatalogContext _catalogContext;

    public SubscriptionService(
        IMaxioClient maxio,
        IOptions<MaxioOptions> options,
        UserManager<ApplicationUser> userManager,
        CatalogContext catalogContext)
    {
        _maxio = maxio;
        _options = options.Value;
        _userManager = userManager;
        _catalogContext = catalogContext;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products
            .Where(IsConfiguredPlan)
            .OrderBy(x => x.PriceInCents)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToPlanDto)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        ClaimsPrincipal principal,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionValidationException("productHandle is required.");
        }

        var user = await ResolveUserAsync(principal);
        productHandle = productHandle.Trim();
        var operationKey = $"{user.Id}:{productHandle.ToUpperInvariant()}";
        var operationLock = OperationLocks.GetOrAdd(operationKey, _ => new SemaphoreSlim(1, 1));
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            var product = await _maxio.GetProductByHandleAsync(productHandle, cancellationToken);
            if (product is null || !IsConfiguredPlan(product))
            {
                throw new SubscriptionValidationException("The requested subscription plan is not available.");
            }

            var customerReference = BuildReference("eshop-user", user.Id);
            var subscriptionReference = BuildReference("eshop-sub", $"{user.Id}:{productHandle.ToUpperInvariant()}");
            var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);

            var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var subscription = subscriptions.FirstOrDefault(x =>
                string.Equals(x.Reference, subscriptionReference, StringComparison.Ordinal));

            if (subscription is null)
            {
                var site = await _maxio.GetSiteAsync(cancellationToken);
                var createRequest = new CreateMaxioSubscription(
                    product.Handle!,
                    customerReference,
                    subscriptionReference,
                    BuildToken(subscriptionReference),
                    site.RelationshipInvoicingEnabled ? "remittance" : "invoice");

                try
                {
                    subscription = await _maxio.CreateSubscriptionAsync(createRequest, cancellationToken);
                }
                catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict || ex.OutcomeMayBeAmbiguous)
                {
                    subscription = await RecoverSubscriptionAsync(
                        customer.Id, subscriptionReference, cancellationToken);
                    if (subscription is null)
                    {
                        throw new MaxioApiException(
                            HttpStatusCode.ServiceUnavailable,
                            "The subscription request outcome is still being confirmed. It is safe to retry.");
                    }
                }
            }

            await SynchronizeRecordAsync(user.Id, customerReference, subscription, cancellationToken);
            return ToSubscriptionDto(subscription);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal);
        var customerReference = BuildReference("eshop-user", user.Id);
        var customer = await _maxio.GetCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = (await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
            .Where(x => x.Product is not null && IsConfiguredPlan(x.Product))
            .OrderByDescending(x => x.Id)
            .ToList();

        foreach (var subscription in subscriptions)
        {
            await SynchronizeRecordAsync(user.Id, customerReference, subscription, cancellationToken);
        }

        return subscriptions.Select(ToSubscriptionDto).ToList();
    }

    private async Task<ApplicationUser> ResolveUserAsync(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        ApplicationUser? user = null;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            user = await _userManager.FindByIdAsync(userId);
        }

        if (user is null && !string.IsNullOrWhiteSpace(principal.Identity?.Name))
        {
            user = await _userManager.FindByNameAsync(principal.Identity.Name);
        }

        return user ?? throw new AuthenticatedUserNotFoundException();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        ApplicationUser user,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName ?? throw new SubscriptionValidationException(
            "The user account must have an email address before subscribing.");
        var localPart = email.Split('@', 2)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;

        try
        {
            return await _maxio.CreateCustomerAsync(
                new CreateMaxioCustomer(firstName, "Customer", email, reference), cancellationToken);
        }
        catch (MaxioApiException ex) when (
            ex.StatusCode == HttpStatusCode.UnprocessableEntity ||
            ex.StatusCode == HttpStatusCode.Conflict ||
            ex.OutcomeMayBeAmbiguous)
        {
            var recovered = await RecoverCustomerAsync(reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw;
        }
    }

    private async Task<MaxioCustomer?> RecoverCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var customer = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
            if (customer is not null) return customer;
            if (attempt < 3) await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
        }

        return null;
    }

    private async Task<MaxioSubscription?> RecoverSubscriptionAsync(
        long customerId,
        string reference,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
            var subscription = subscriptions.FirstOrDefault(x =>
                string.Equals(x.Reference, reference, StringComparison.Ordinal));
            if (subscription is not null) return subscription;
            if (attempt < 4) await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        return null;
    }

    private async Task SynchronizeRecordAsync(
        string userId,
        string customerReference,
        MaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        if (subscription.Product?.Handle is null || string.IsNullOrWhiteSpace(subscription.Reference))
        {
            return;
        }

        var record = await _catalogContext.SubscriptionRecords.FirstOrDefaultAsync(x =>
            x.UserId == userId && x.ProductHandle == subscription.Product.Handle, cancellationToken);
        var nextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt;
        if (record is null)
        {
            record = new SubscriptionRecord(
                userId,
                subscription.Customer.Id,
                customerReference,
                subscription.Id,
                subscription.Reference,
                subscription.Product.Handle,
                subscription.State,
                nextBillingAt);
            _catalogContext.SubscriptionRecords.Add(record);
        }
        else
        {
            record.Synchronize(
                subscription.Customer.Id,
                customerReference,
                subscription.Id,
                subscription.Reference,
                subscription.State,
                nextBillingAt);
        }

        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (record.Id == 0)
        {
            _catalogContext.Entry(record).State = EntityState.Detached;
            var concurrentRecord = await _catalogContext.SubscriptionRecords.FirstOrDefaultAsync(x =>
                x.UserId == userId && x.ProductHandle == subscription.Product.Handle, cancellationToken);
            if (concurrentRecord is null) throw;
            concurrentRecord.Synchronize(
                subscription.Customer.Id,
                customerReference,
                subscription.Id,
                subscription.Reference,
                subscription.State,
                nextBillingAt);
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
    }

    private bool IsConfiguredPlan(MaxioProduct product) =>
        product.ArchivedAt is null &&
        !string.IsNullOrWhiteSpace(product.Handle) &&
        string.Equals(product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase);

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new(
        product.Handle!,
        product.Name,
        product.Description,
        product.PriceInCents,
        product.PriceInCents / 100m,
        product.Interval,
        product.IntervalUnit,
        product.RequireCreditCard);

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription)
    {
        var product = subscription.Product ?? throw new InvalidOperationException(
            "A catalog subscription did not include its product.");
        return new SubscriptionDto(
            subscription.Id,
            product.Handle ?? string.Empty,
            product.Name,
            subscription.ProductPriceInCents,
            subscription.ProductPriceInCents / 100m,
            product.Interval,
            product.IntervalUnit,
            subscription.State,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    private static string BuildReference(string prefix, string value) =>
        $"{prefix}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";

    private static string BuildToken(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"subscribe:{value}"))).ToLowerInvariant();
}
