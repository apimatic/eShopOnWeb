using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan PendingEnrollmentTimeout = TimeSpan.FromMinutes(2);
    private readonly IMaxioClient _maxioClient;
    private readonly CatalogContext _catalogContext;
    private readonly SubscriptionOperationLock _operationLock;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioClient maxioClient,
        CatalogContext catalogContext,
        SubscriptionOperationLock operationLock,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _catalogContext = catalogContext;
        _operationLock = operationLock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxioClient.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .OrderBy(product => product.PriceInCents)
            .Select(ToPlanDto)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle) || productHandle.Length > 255)
        {
            throw new ArgumentException("A valid product handle is required.", nameof(productHandle));
        }

        if (string.IsNullOrWhiteSpace(shopper.UserId) || shopper.UserId.Length > 128)
        {
            throw new ArgumentException("The authenticated user identifier is invalid.", nameof(shopper));
        }

        productHandle = productHandle.Trim();
        using var operation = await _operationLock.AcquireAsync(
            $"{shopper.UserId}:{productHandle}",
            cancellationToken);

        var products = await _maxioClient.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        var product = products.SingleOrDefault(candidate =>
            candidate.ArchivedAt is null &&
            string.Equals(candidate.Handle, productHandle, StringComparison.Ordinal));

        if (product is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var subscriptionReference = BuildSubscriptionReference(shopper.UserId, productHandle);
        var enrollment = await _catalogContext.SubscriptionEnrollments.SingleOrDefaultAsync(
            candidate => candidate.UserId == shopper.UserId && candidate.ProductHandle == productHandle,
            cancellationToken);

        if (enrollment?.IsComplete == true)
        {
            var current = await _maxioClient.ReadSubscriptionAsync(
                enrollment.MaxioSubscriptionId!.Value,
                cancellationToken);

            current ??= await _maxioClient.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (current is not null)
            {
                return ToSubscriptionDto(current);
            }

            _catalogContext.SubscriptionEnrollments.Remove(enrollment);
            await _catalogContext.SaveChangesAsync(cancellationToken);
            enrollment = null;
        }

        if (enrollment is not null)
        {
            var reconciled = await _maxioClient.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (reconciled is not null)
            {
                await CompleteEnrollmentAsync(enrollment, reconciled, cancellationToken);
                return ToSubscriptionDto(reconciled);
            }

            if (enrollment.PendingSince > DateTimeOffset.UtcNow - PendingEnrollmentTimeout)
            {
                throw new SubscriptionEnrollmentInProgressException();
            }

            enrollment.Claim(Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow);
            try
            {
                await _catalogContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new SubscriptionEnrollmentInProgressException();
            }
        }
        else
        {
            var reconciled = await _maxioClient.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (reconciled is not null)
            {
                await PersistReconciledEnrollmentAsync(shopper, productHandle, subscriptionReference, reconciled, cancellationToken);
                return ToSubscriptionDto(reconciled);
            }

            enrollment = new SubscriptionEnrollment(
                shopper.UserId,
                productHandle,
                subscriptionReference,
                Guid.NewGuid().ToString("D"),
                DateTimeOffset.UtcNow);
            _catalogContext.SubscriptionEnrollments.Add(enrollment);

            try
            {
                await _catalogContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _catalogContext.Entry(enrollment).State = EntityState.Detached;
                throw new SubscriptionEnrollmentInProgressException();
            }
        }

        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            MaxioSubscription subscription;

            try
            {
                subscription = await _maxioClient.CreateSubscriptionAsync(
                    new MaxioSubscriptionDetails(productHandle, customer.Id, subscriptionReference),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is MaxioApiException or HttpRequestException)
            {
                var reconciled = await _maxioClient.FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (reconciled is null)
                {
                    if (exception is MaxioApiException apiException &&
                        (int)apiException.StatusCode is >= 400 and < 500)
                    {
                        _catalogContext.SubscriptionEnrollments.Remove(enrollment);
                        await _catalogContext.SaveChangesAsync(cancellationToken);
                    }

                    throw;
                }

                subscription = reconciled;
            }

            await CompleteEnrollmentAsync(enrollment, subscription, cancellationToken);
            return ToSubscriptionDto(subscription);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            _logger.LogWarning(
                "Subscription enrollment failed for user {UserId} and product {ProductHandle}; the pending claim is retained when the outcome may be ambiguous.",
                shopper.UserId,
                productHandle);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken)
    {
        var customer = await _maxioClient.FindCustomerAsync(BuildCustomerReference(shopper.UserId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(subscription => subscription.Id)
            .Select(ToSubscriptionDto)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(shopper.UserId);
        var customer = await _maxioClient.FindCustomerAsync(reference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = GetCustomerName(shopper);
        try
        {
            return await _maxioClient.CreateCustomerAsync(
                new MaxioCustomerDetails(firstName, lastName, shopper.Email, reference),
                cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            customer = await _maxioClient.FindCustomerAsync(reference, cancellationToken);
            if (customer is null)
            {
                throw;
            }

            return customer;
        }
    }

    private async Task CompleteEnrollmentAsync(
        SubscriptionEnrollment enrollment,
        MaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        enrollment.Complete(subscription.Customer.Id, subscription.Id, DateTimeOffset.UtcNow);
        await _catalogContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistReconciledEnrollmentAsync(
        ShopperIdentity shopper,
        string productHandle,
        string subscriptionReference,
        MaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        var enrollment = new SubscriptionEnrollment(
            shopper.UserId,
            productHandle,
            subscriptionReference,
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow);
        enrollment.Complete(subscription.Customer.Id, subscription.Id, DateTimeOffset.UtcNow);
        _catalogContext.SubscriptionEnrollments.Add(enrollment);

        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _catalogContext.Entry(enrollment).State = EntityState.Detached;
        }
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product)
    {
        return new SubscriptionPlanDto(
            product.Id,
            product.Handle!,
            product.Name,
            product.Description,
            product.PriceInCents,
            product.PriceInCents / 100m,
            product.Interval,
            product.IntervalUnit,
            product.RequireCreditCard);
    }

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription)
    {
        return new SubscriptionDto(
            subscription.Id,
            subscription.Customer.Id,
            subscription.Product.Handle ?? string.Empty,
            subscription.Product.Name,
            subscription.ProductPriceInCents,
            subscription.ProductPriceInCents / 100m,
            subscription.Product.Interval,
            subscription.Product.IntervalUnit,
            subscription.State,
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt);
    }

    private static string BuildCustomerReference(string userId) => $"eshop-user:{userId}";

    private static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"eshop-subscription:{userId}:{productHandle}";

    private static (string FirstName, string LastName) GetCustomerName(ShopperIdentity shopper)
    {
        var source = shopper.UserName.Split('@')[0];
        var parts = source.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            >= 2 => (parts[0], string.Join(" ", parts.Skip(1))),
            1 => (parts[0], "Customer"),
            _ => ("eShop", "Customer")
        };
    }
}
