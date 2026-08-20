using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new();
    private readonly CatalogContext _catalogContext;
    private readonly MaxioClient _client;
    private readonly MaxioOptions _options;

    public SubscriptionBillingService(
        CatalogContext catalogContext,
        MaxioClient client,
        IOptions<MaxioOptions> options)
    {
        _catalogContext = catalogContext;
        _client = client;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return (await ListFamilyProductsAsync(cancellationToken))
                .Select(MapPlan)
                .OrderBy(plan => plan.PriceInCents)
                .ToArray();
        }
        catch (MaxioApiException exception)
        {
            throw Wrap(exception);
        }
        catch (HttpRequestException exception)
        {
            throw Unreachable(exception);
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscriptionUser user,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var lockKey = $"{user.Id}:{productHandle}";
        var gate = SubscriptionLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            return await SubscribeCoreAsync(user, productHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var customer = await _client.FindCustomerAsync(userId, cancellationToken);
            if (customer is null)
            {
                return Array.Empty<SubscriptionDetails>();
            }

            var subscriptions = (await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .Where(IsConfiguredFamily)
                .ToArray();

            await SynchronizeRecordsAsync(userId, customer.Id, subscriptions, cancellationToken);
            return subscriptions.Select(MapSubscription).ToArray();
        }
        catch (MaxioApiException exception)
        {
            throw Wrap(exception);
        }
        catch (HttpRequestException exception)
        {
            throw Unreachable(exception);
        }
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(
        SubscriptionUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var product = (await ListFamilyProductsAsync(cancellationToken))
                .SingleOrDefault(candidate => string.Equals(
                    candidate.Handle,
                    productHandle,
                    StringComparison.Ordinal));
            if (product is null)
            {
                throw new SubscriptionPlanNotFoundException(productHandle);
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var reference = SubscriptionReference(user.Id, productHandle);
            var existing = (await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .FirstOrDefault(subscription =>
                    string.Equals(subscription.Reference, reference, StringComparison.Ordinal) ||
                    (IsConfiguredFamily(subscription) && string.Equals(
                        subscription.Product.Handle,
                        productHandle,
                        StringComparison.Ordinal)));

            if (existing is not null)
            {
                await SynchronizeRecordAsync(user.Id, customer.Id, existing, cancellationToken);
                return new SubscribeResult(MapSubscription(existing), true);
            }

            await EnsurePendingRecordAsync(user.Id, productHandle, cancellationToken);

            MaxioSubscription created;
            try
            {
                created = await _client.CreateSubscriptionAsync(
                    new CreateSubscriptionRequest(new CreateSubscription(
                        productHandle,
                        user.Id,
                        reference,
                        "remittance")),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is MaxioApiException or HttpRequestException)
            {
                // POST responses can be lost after Maxio commits. Reconcile before surfacing an error;
                // retrying the POST itself would risk creating a duplicate subscription.
                var reconciled = (await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                    .FirstOrDefault(subscription => string.Equals(
                        subscription.Reference,
                        reference,
                        StringComparison.Ordinal));
                if (reconciled is null)
                {
                    throw;
                }

                created = reconciled;
            }

            await SynchronizeRecordAsync(user.Id, customer.Id, created, cancellationToken);
            return new SubscribeResult(MapSubscription(created), false);
        }
        catch (SubscriptionPlanNotFoundException)
        {
            throw;
        }
        catch (MaxioApiException exception)
        {
            throw Wrap(exception);
        }
        catch (HttpRequestException exception)
        {
            throw Unreachable(exception);
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        SubscriptionUser user,
        CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerAsync(user.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = CustomerName(user.UserName);
        try
        {
            return await _client.CreateCustomerAsync(
                new CreateCustomerRequest(new CreateCustomer(firstName, lastName, user.Email, user.Id)),
                cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Customer reference is unique according to the Maxio contract. A concurrent creator wins;
            // reading by that reference makes customer creation idempotent across app instances.
            var concurrentlyCreated = await _client.FindCustomerAsync(user.Id, cancellationToken);
            if (concurrentlyCreated is null)
            {
                throw;
            }

            return concurrentlyCreated;
        }
        catch (HttpRequestException)
        {
            // Handle an ambiguous network failure after Maxio may have committed the customer.
            var reconciled = await _client.FindCustomerAsync(user.Id, cancellationToken);
            if (reconciled is null)
            {
                throw;
            }

            return reconciled;
        }
    }

    private async Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(CancellationToken cancellationToken)
    {
        return (await _client.ListProductsAsync(cancellationToken))
            .Where(product => product.ArchivedAt is null &&
                              product.Handle is not null &&
                              string.Equals(
                                  product.ProductFamily.Handle,
                                  _options.ProductFamilyHandle,
                                  StringComparison.Ordinal))
            .ToArray();
    }

    private async Task EnsurePendingRecordAsync(
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (await _catalogContext.SubscriptionRecords.AnyAsync(
                record => record.UserId == userId && record.ProductHandle == productHandle,
                cancellationToken))
        {
            return;
        }

        _catalogContext.SubscriptionRecords.Add(new SubscriptionRecord(userId, productHandle));
        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _catalogContext.ChangeTracker.Clear();
            if (!await _catalogContext.SubscriptionRecords.AnyAsync(
                    record => record.UserId == userId && record.ProductHandle == productHandle,
                    cancellationToken))
            {
                throw;
            }
        }
    }

    private async Task SynchronizeRecordsAsync(
        string userId,
        int customerId,
        IEnumerable<MaxioSubscription> subscriptions,
        CancellationToken cancellationToken)
    {
        foreach (var subscription in subscriptions)
        {
            await SynchronizeRecordAsync(userId, customerId, subscription, cancellationToken, false);
        }

        await _catalogContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SynchronizeRecordAsync(
        string userId,
        int customerId,
        MaxioSubscription subscription,
        CancellationToken cancellationToken,
        bool save = true)
    {
        var handle = subscription.Product.Handle ?? string.Empty;
        var record = await _catalogContext.SubscriptionRecords.SingleOrDefaultAsync(
            candidate => candidate.UserId == userId && candidate.ProductHandle == handle,
            cancellationToken);
        if (record is null)
        {
            record = new SubscriptionRecord(userId, handle);
            _catalogContext.SubscriptionRecords.Add(record);
        }

        record.Synchronize(customerId, subscription.Id);
        if (save)
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
    }

    private bool IsConfiguredFamily(MaxioSubscription subscription) => string.Equals(
        subscription.Product.ProductFamily.Handle,
        _options.ProductFamilyHandle,
        StringComparison.Ordinal);

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new(
        product.Handle!,
        product.Name,
        product.Description,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit,
        product.RequireCreditCard);

    private static SubscriptionDetails MapSubscription(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.Product.Handle ?? string.Empty,
        subscription.Product.Name,
        subscription.ProductPriceInCents,
        subscription.Currency,
        subscription.State,
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);

    private static string SubscriptionReference(string userId, string productHandle) =>
        $"eshop:{userId}:{productHandle}";

    private static (string FirstName, string LastName) CustomerName(string userName)
    {
        var localPart = userName.Split('@', 2)[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("eShop", "Customer"),
            1 => (parts[0], "Customer"),
            _ => (parts[0], string.Join(' ', parts.Skip(1)))
        };
    }

    private static SubscriptionBillingException Wrap(MaxioApiException exception) => new(
        $"Maxio rejected the billing request: {exception.Message}",
        exception);

    private static SubscriptionBillingException Unreachable(HttpRequestException exception) => new(
        "Maxio is temporarily unavailable.",
        exception);
}
