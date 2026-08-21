using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private readonly IMaxioClient _maxio;
    private readonly IRepository<MaxioSubscriptionRecord> _records;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionRequestLock _requestLock;
    private readonly MaxioOptions _options;

    public SubscriptionBillingService(
        IMaxioClient maxio,
        IRepository<MaxioSubscriptionRecord> records,
        UserManager<ApplicationUser> userManager,
        ISubscriptionRequestLock requestLock,
        IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _records = records;
        _userManager = userManager;
        _requestLock = requestLock;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null &&
                              string.Equals(product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            .OrderBy(product => product.PriceInCents)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        ClaimsPrincipal principal,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal);
        productHandle = productHandle.Trim();

        var products = await _maxio.ListProductsAsync(cancellationToken);
        var product = products.SingleOrDefault(candidate =>
            candidate.ArchivedAt is null &&
            string.Equals(candidate.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal) &&
            string.Equals(candidate.Handle, productHandle, StringComparison.Ordinal));
        if (product is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var customerReference = BuildCustomerReference(user.Id);
        var subscriptionReference = BuildSubscriptionReference(user.Id, productHandle);
        using var acquiredLock = await _requestLock.AcquireAsync(subscriptionReference, cancellationToken);

        var record = await _records.FirstOrDefaultAsync(
            new MaxioSubscriptionRecordSpecification(user.Id, productHandle),
            cancellationToken);
        if (record is not null)
        {
            var recordedSubscription = await _maxio.ReadSubscriptionAsync(record.MaxioSubscriptionId, cancellationToken);
            if (recordedSubscription is not null)
            {
                ValidateOwnership(recordedSubscription, customerReference, productHandle);
                return MapSubscription(recordedSubscription);
            }
        }

        var existingSubscription = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existingSubscription is not null)
        {
            ValidateOwnership(existingSubscription, customerReference, productHandle);
            await SaveRecordAsync(record, user.Id, productHandle, customerReference, existingSubscription, cancellationToken);
            return MapSubscription(existingSubscription);
        }

        var customer = await GetOrCreateCustomerAsync(user, customerReference, cancellationToken);
        MaxioSubscription subscription;
        try
        {
            subscription = await _maxio.CreateSubscriptionAsync(
                new MaxioCreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerReference = customerReference,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = "remittance"
                },
                cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var recoveredSubscription = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (recoveredSubscription is null)
            {
                throw;
            }

            subscription = recoveredSubscription;
        }

        ValidateOwnership(subscription, customerReference, productHandle);
        await SaveRecordAsync(record, user.Id, productHandle, customerReference, subscription, cancellationToken);
        return MapSubscription(subscription);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal);
        var customer = await _maxio.FindCustomerAsync(BuildCustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return [];
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(subscription => string.Equals(
                subscription.Product.ProductFamily.Handle,
                _options.ProductFamilyHandle,
                StringComparison.Ordinal))
            .OrderBy(subscription => subscription.Id)
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(
        ApplicationUser user,
        string reference,
        CancellationToken cancellationToken)
    {
        var existingCustomer = await _maxio.FindCustomerAsync(reference, cancellationToken);
        if (existingCustomer is not null)
        {
            return existingCustomer;
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("The authenticated user does not have an email address.");
        }

        var localPart = email.Split('@', 2)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;
        try
        {
            return await _maxio.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    FirstName = firstName,
                    LastName = "Customer",
                    Email = email,
                    Reference = reference
                },
                cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var recoveredCustomer = await _maxio.FindCustomerAsync(reference, cancellationToken);
            if (recoveredCustomer is null)
            {
                throw;
            }

            return recoveredCustomer;
        }
    }

    private async Task<ApplicationUser> ResolveUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new SubscriptionIdentityException();
        }

        return await _userManager.FindByNameAsync(username) ?? throw new SubscriptionIdentityException();
    }

    private async Task SaveRecordAsync(
        MaxioSubscriptionRecord? record,
        string userId,
        string productHandle,
        string customerReference,
        MaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        if (record is null)
        {
            await _records.AddAsync(
                new MaxioSubscriptionRecord(
                    userId,
                    productHandle,
                    customerReference,
                    subscription.Customer.Id,
                    subscription.Reference!,
                    subscription.Id),
                cancellationToken);
            return;
        }

        record.UpdateMaxioIds(subscription.Customer.Id, subscription.Id);
        await _records.UpdateAsync(record, cancellationToken);
    }

    private static void ValidateOwnership(
        MaxioSubscription subscription,
        string customerReference,
        string productHandle)
    {
        if (!string.Equals(subscription.Customer.Reference, customerReference, StringComparison.Ordinal) ||
            !string.Equals(subscription.Product.Handle, productHandle, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(subscription.Reference))
        {
            throw new InvalidOperationException("The Maxio subscription reference resolved to an unexpected owner or product.");
        }
    }

    private static string BuildCustomerReference(string userId) => $"eshop-user-{userId}";

    private static string BuildSubscriptionReference(string userId, string productHandle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}\n{productHandle}"));
        return $"eshop-sub-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference!,
        PlanHandle = subscription.Product.Handle!,
        PlanName = subscription.Product.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Interval = subscription.Product.Interval,
        IntervalUnit = subscription.Product.IntervalUnit,
        State = subscription.State,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
    };
}
