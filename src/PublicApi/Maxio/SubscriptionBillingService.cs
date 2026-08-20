using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<CreateSubscriptionResult> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken);
}

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan ReservationLease = TimeSpan.FromMinutes(2);
    private readonly IMaxioClient _maxio;
    private readonly CatalogContext _dbContext;
    private readonly SubscriptionOperationLock _operationLock;
    private readonly string _productFamilyHandle;

    public SubscriptionBillingService(
        IMaxioClient maxio,
        CatalogContext dbContext,
        SubscriptionOperationLock operationLock,
        IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _dbContext = dbContext;
        _operationLock = operationLock;
        _productFamilyHandle = options.Value.ProductFamilyHandle;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products
            .Where(IsAvailablePlan)
            .OrderBy(product => product.PriceInCents)
            .ThenBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToPlanDto)
            .ToList();
    }

    public async Task<CreateSubscriptionResult> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        productHandle = productHandle.Trim();
        var plan = await _maxio.ReadProductByHandleAsync(productHandle, cancellationToken);
        if (plan is null || !IsAvailablePlan(plan))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        if (plan.RequireCreditCard)
        {
            throw new SubscriptionPlanRequiresPaymentException(productHandle);
        }

        var subscriptionReference = CreateSubscriptionReference(shopper.UserId, plan.Handle!);
        using var operation = await _operationLock.AcquireAsync(subscriptionReference, cancellationToken);

        var existingSubscription = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existingSubscription is not null)
        {
            await SaveCompletedMappingAsync(shopper.UserId, plan.Handle!, subscriptionReference, existingSubscription, cancellationToken);
            return new CreateSubscriptionResult(ToSubscriptionDto(existingSubscription), false);
        }

        var (record, ownsReservation) = await ReserveAsync(
            shopper.UserId, plan.Handle!, subscriptionReference, cancellationToken);
        if (!ownsReservation)
        {
            if (record.MaxioSubscriptionId is int knownSubscriptionId)
            {
                var knownSubscription = await _maxio.ReadSubscriptionAsync(knownSubscriptionId, cancellationToken);
                if (knownSubscription is not null)
                {
                    return new CreateSubscriptionResult(ToSubscriptionDto(knownSubscription), false);
                }
            }

            throw new SubscriptionCreationInProgressException();
        }

        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            record.SetCustomer(customer.Id);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var created = await _maxio.CreateSubscriptionAsync(
                new MaxioCreateSubscription(plan.Handle!, customer.Id, subscriptionReference, "remittance"), cancellationToken);
            record.Complete(customer.Id, created.Id);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new CreateSubscriptionResult(ToSubscriptionDto(created), true);
        }
        catch (OperationCanceledException)
        {
            // Keep the reservation until its lease expires. The remote result is unknown.
            throw;
        }
        catch (Exception)
        {
            var (recovered, reconciliationSucceeded) = await TryRecoverSubscriptionAsync(subscriptionReference, cancellationToken);
            if (recovered is not null)
            {
                record.Complete(recovered.Customer.Id, recovered.Id);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new CreateSubscriptionResult(ToSubscriptionDto(recovered), false);
            }

            // Only release the reservation after Maxio definitively reports no match. If
            // reconciliation itself fails, preserve the lease to prevent a blind retry.
            if (reconciliationSucceeded)
            {
                record.Fail();
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken)
    {
        var customer = await _maxio.ReadCustomerByReferenceAsync(CreateCustomerReference(shopper.UserId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(subscription => IsInConfiguredFamily(subscription.Product))
            .OrderByDescending(subscription => subscription.Id)
            .Select(ToSubscriptionDto)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var reference = CreateCustomerReference(shopper.UserId);
        var existing = await _maxio.ReadCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var firstName = shopper.Email.Split('@', 2)[0];
        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = "eShopOnWeb";
        }

        try
        {
            return await _maxio.CreateCustomerAsync(
                new MaxioCreateCustomer(firstName, "Customer", shopper.Email, reference), cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio enforces customer-reference uniqueness. A concurrent creator may have won.
            var concurrentlyCreated = await _maxio.ReadCustomerByReferenceAsync(reference, cancellationToken);
            if (concurrentlyCreated is null)
            {
                throw;
            }

            return concurrentlyCreated;
        }
    }

    private async Task<(SubscriptionBillingRecord Record, bool OwnsReservation)> ReserveAsync(
        string userId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var record = await _dbContext.SubscriptionBillingRecords.SingleOrDefaultAsync(
            candidate => candidate.UserId == userId && candidate.ProductHandle == productHandle,
            cancellationToken);

        if (record is null)
        {
            record = new SubscriptionBillingRecord(
                userId, productHandle, subscriptionReference, Guid.NewGuid(), now.Add(ReservationLease));
            _dbContext.SubscriptionBillingRecords.Add(record);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return (record, true);
            }
            catch (DbUpdateException)
            {
                _dbContext.ChangeTracker.Clear();
                record = await _dbContext.SubscriptionBillingRecords.SingleAsync(
                    candidate => candidate.UserId == userId && candidate.ProductHandle == productHandle,
                    cancellationToken);
            }
        }

        if (record.Status == SubscriptionBillingStatus.Completed || record.HasActiveLease(now))
        {
            return (record, false);
        }

        record.Claim(Guid.NewGuid(), now.Add(ReservationLease));
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return (record, true);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (record, false);
        }
    }

    private async Task SaveCompletedMappingAsync(
        string userId,
        string productHandle,
        string subscriptionReference,
        MaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        var record = await _dbContext.SubscriptionBillingRecords.SingleOrDefaultAsync(
            candidate => candidate.UserId == userId && candidate.ProductHandle == productHandle,
            cancellationToken);
        if (record is null)
        {
            record = new SubscriptionBillingRecord(
                userId, productHandle, subscriptionReference, Guid.NewGuid(), DateTimeOffset.MinValue);
            _dbContext.SubscriptionBillingRecords.Add(record);
        }

        record.Complete(subscription.Customer.Id, subscription.Id);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            record = await _dbContext.SubscriptionBillingRecords.SingleAsync(
                candidate => candidate.UserId == userId && candidate.ProductHandle == productHandle,
                cancellationToken);
            record.Complete(subscription.Customer.Id, subscription.Id);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<(MaxioSubscription? Subscription, bool ReconciliationSucceeded)> TryRecoverSubscriptionAsync(
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken), true);
        }
        catch (MaxioApiException)
        {
            return (null, false);
        }
    }

    private bool IsAvailablePlan(MaxioProduct product) =>
        product.ArchivedAt is null &&
        !string.IsNullOrWhiteSpace(product.Handle) &&
        IsInConfiguredFamily(product);

    private bool IsInConfiguredFamily(MaxioProduct product) =>
        string.Equals(product.ProductFamily.Handle, _productFamilyHandle, StringComparison.OrdinalIgnoreCase);

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) =>
        new(product.Handle!, product.Name, product.Description, product.PriceInCents,
            product.Interval, product.IntervalUnit, product.RequireCreditCard);

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription) =>
        new(subscription.Id, subscription.Product.Handle ?? string.Empty, subscription.Product.Name,
            subscription.ProductPriceInCents, subscription.Currency, subscription.Product.Interval,
            subscription.Product.IntervalUnit, subscription.State,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);

    private static string CreateCustomerReference(string userId) => $"eshop-user:{userId}";

    private static string CreateSubscriptionReference(string userId, string productHandle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}\n{productHandle.ToUpperInvariant()}"));
        return $"eshop-sub:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }
}
