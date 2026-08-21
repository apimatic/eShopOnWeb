using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioClient _maxio;
    private readonly AppIdentityDbContext _dbContext;
    private readonly MaxioOptions _options;

    public SubscriptionService(
        IMaxioClient maxio,
        AppIdentityDbContext dbContext,
        IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(x => x.ArchivedAt is null && !string.IsNullOrWhiteSpace(x.Handle))
            .OrderBy(x => x.PriceInCents)
            .Select(ToPlanDto)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        var product = products.SingleOrDefault(x =>
            x.ArchivedAt is null && string.Equals(x.Handle, productHandle, StringComparison.Ordinal));
        if (product is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        if (product.RequireCreditCard)
        {
            throw new SubscriptionPaymentMethodRequiredException(productHandle);
        }

        var customerReference = CustomerReference(user.Id);
        var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
        var subscriptionReference = SubscriptionReference(user.Id, productHandle);

        var existing = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existing is not null &&
            (existing.Customer?.Id != customer.Id ||
             !string.Equals(existing.Product?.Handle, productHandle, StringComparison.Ordinal)))
        {
            existing = null;
        }

        existing ??= (await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
            .FirstOrDefault(x => string.Equals(x.Product?.Handle, productHandle, StringComparison.Ordinal));

        if (existing is not null)
        {
            await PersistSubscriptionAsync(user.Id, subscriptionReference, existing, cancellationToken);
            return new SubscribeResult(ToSubscriptionDto(existing), false);
        }

        var site = await _maxio.GetSiteAsync(cancellationToken);
        var paymentCollectionMethod = site.RelationshipInvoicingEnabled ? "remittance" : "invoice";
        MaxioSubscription subscription;
        try
        {
            subscription = await _maxio.CreateSubscriptionAsync(
                customerReference,
                productHandle,
                subscriptionReference,
                paymentCollectionMethod,
                UniquenessToken($"subscription:{_options.Subdomain}:{user.Id}:{productHandle}:{paymentCollectionMethod}"),
                cancellationToken);
        }
        catch (Exception exception) when (CanRecoverSubscriptionCreate(exception, cancellationToken))
        {
            var recovered = await RecoverSubscriptionAsync(
                customer.Id,
                subscriptionReference,
                productHandle,
                cancellationToken);
            if (recovered is null)
            {
                throw;
            }

            subscription = recovered;
        }

        await PersistSubscriptionAsync(user.Id, subscriptionReference, subscription, cancellationToken);
        return new SubscribeResult(ToSubscriptionDto(subscription), true);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken)
    {
        var customerReference = CustomerReference(user.Id);
        var customer = await _maxio.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return [];
        }

        await PersistCustomerAsync(user.Id, customerReference, customer.Id, cancellationToken);
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        foreach (var subscription in subscriptions)
        {
            var reference = string.IsNullOrWhiteSpace(subscription.Reference)
                ? SubscriptionReference(user.Id, subscription.Product?.Handle ?? subscription.Id.ToString())
                : subscription.Reference;
            await PersistSubscriptionAsync(user.Id, reference, subscription, cancellationToken);
        }

        return subscriptions.Select(ToSubscriptionDto).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        BillingUser user,
        string reference,
        CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerAsync(reference, cancellationToken);
        if (customer is null)
        {
            var (firstName, lastName) = CustomerName(user.UserName);
            try
            {
                customer = await _maxio.CreateCustomerAsync(
                    new MaxioCustomerInput(firstName, lastName, user.Email, reference),
                    UniquenessToken($"customer:{_options.Subdomain}:{user.Id}"),
                    cancellationToken);
            }
            catch (Exception exception) when (CanRecoverCustomerCreate(exception, cancellationToken))
            {
                customer = await _maxio.FindCustomerAsync(reference, cancellationToken);
                if (customer is null)
                {
                    throw;
                }
            }
        }

        await PersistCustomerAsync(user.Id, reference, customer.Id, cancellationToken);
        return customer;
    }

    private async Task<MaxioSubscription?> RecoverSubscriptionAsync(
        long customerId,
        string subscriptionReference,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var delays = new[] { 0, 100, 300, 1_000 };
        foreach (var delay in delays)
        {
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken);
            }

            var byReference = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (byReference is not null)
            {
                return byReference;
            }

            var byCustomer = (await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken))
                .FirstOrDefault(x => string.Equals(x.Product?.Handle, productHandle, StringComparison.Ordinal));
            if (byCustomer is not null)
            {
                return byCustomer;
            }
        }

        return null;
    }

    private async Task PersistCustomerAsync(
        string userId,
        string reference,
        long maxioCustomerId,
        CancellationToken cancellationToken)
    {
        var record = await _dbContext.MaxioCustomers.SingleOrDefaultAsync(
            x => x.UserId == userId && x.SiteSubdomain == _options.Subdomain,
            cancellationToken);
        if (record is null)
        {
            _dbContext.MaxioCustomers.Add(new MaxioCustomerRecord
            {
                UserId = userId,
                SiteSubdomain = _options.Subdomain,
                CustomerReference = reference,
                MaxioCustomerId = maxioCustomerId,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            record.CustomerReference = reference;
            record.MaxioCustomerId = maxioCustomerId;
            record.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await SaveMappingAsync(cancellationToken);
    }

    private async Task PersistSubscriptionAsync(
        string userId,
        string reference,
        MaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        if (subscription.Product?.Handle is not { Length: > 0 } productHandle)
        {
            return;
        }

        var record = await _dbContext.MaxioSubscriptions.SingleOrDefaultAsync(
            x => x.UserId == userId &&
                 x.SiteSubdomain == _options.Subdomain &&
                 x.ProductHandle == productHandle,
            cancellationToken);
        if (record is null)
        {
            record = new MaxioSubscriptionRecord
            {
                UserId = userId,
                SiteSubdomain = _options.Subdomain,
                ProductHandle = productHandle
            };
            _dbContext.MaxioSubscriptions.Add(record);
        }

        record.SubscriptionReference = reference;
        record.MaxioSubscriptionId = subscription.Id;
        record.ProductName = subscription.Product.Name;
        record.PriceInCents = subscription.ProductPriceInCents;
        record.Interval = subscription.Product.Interval;
        record.IntervalUnit = subscription.Product.IntervalUnit;
        record.State = subscription.State;
        record.NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveMappingAsync(cancellationToken);
    }

    private async Task SaveMappingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            // Another app instance won the unique-key race after the Maxio operation.
            _dbContext.ChangeTracker.Clear();
        }
    }

    private static bool CanRecoverCustomerCreate(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException ||
        (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested) ||
        exception is MaxioApiException { StatusCode: HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity };

    private static bool CanRecoverSubscriptionCreate(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException ||
        (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested) ||
        exception is MaxioApiException { StatusCode: HttpStatusCode.Conflict };

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new(
        product.Handle!,
        product.Name,
        product.Description,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit,
        product.RequireCreditCard);

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription)
    {
        var product = subscription.Product ?? new MaxioProduct { Name = "Unknown plan" };
        return new SubscriptionDto(
            subscription.Id,
            product.Handle ?? string.Empty,
            product.Name,
            subscription.ProductPriceInCents,
            product.Interval,
            product.IntervalUnit,
            subscription.State,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    private static string CustomerReference(string userId) =>
        $"eshoponweb-customer-{UniquenessToken($"customer-reference:{userId}")}";

    private static string SubscriptionReference(string userId, string productHandle) =>
        $"eshoponweb-subscription-{UniquenessToken($"subscription-reference:{userId}:{productHandle}")}";

    private static string UniquenessToken(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes.AsSpan(0, 16)).ToString();
    }

    private static (string FirstName, string LastName) CustomerName(string userName)
    {
        var localPart = userName.Split('@', 2)[0];
        var names = localPart.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        return names.Length switch
        {
            >= 2 => (names[0], names[^1]),
            1 => (names[0], "Customer"),
            _ => ("eShopOnWeb", "Customer")
        };
    }
}
