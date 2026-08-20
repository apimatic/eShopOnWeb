using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();
    private static readonly TimeSpan ConcurrentRequestWait = TimeSpan.FromSeconds(15);
    private readonly MaxioClient _client;
    private readonly SubscriptionEnrollmentStore _store;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioClient client,
        SubscriptionEnrollmentStore store,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var products = await _client.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
            return products
                .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
                .OrderBy(product => product.PriceInCents)
                .Select(ToPlan)
                .ToList();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsRemoteFailure(exception))
        {
            throw CreateUnavailableException(exception);
        }
    }

    public async Task<BillingSubscription> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingValidationException("A productHandle is required.");
        }

        var lockKey = $"{user.UserId}\n{productHandle}";
        var enrollmentLock = EnrollmentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await enrollmentLock.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeUnderLockAsync(user, productHandle, cancellationToken);
        }
        finally
        {
            enrollmentLock.Release();
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var customer = await _client.FindCustomerAsync(CustomerReference(user.UserId), cancellationToken);
            if (customer is null)
            {
                return Array.Empty<BillingSubscription>();
            }

            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            return subscriptions
                .Where(subscription => string.Equals(
                    subscription.Product?.ProductFamily.Handle,
                    _options.ProductFamilyHandle,
                    StringComparison.OrdinalIgnoreCase))
                .Select(ToSubscription)
                .ToList();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsRemoteFailure(exception))
        {
            throw CreateUnavailableException(exception);
        }
    }

    private async Task<BillingSubscription> SubscribeUnderLockAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        MaxioProduct product;
        try
        {
            product = await _client.FindProductAsync(productHandle, cancellationToken)
                ?? throw new BillingValidationException("The selected subscription plan does not exist.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsRemoteFailure(exception))
        {
            throw CreateUnavailableException(exception);
        }

        if (product.ArchivedAt is not null ||
            !string.Equals(product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingValidationException("The selected subscription plan is not available.");
        }

        var subscriptionReference = SubscriptionReference(user.UserId, productHandle);
        var lease = await _store.TryAcquireAsync(
            user.UserId,
            productHandle,
            subscriptionReference,
            cancellationToken);

        if (!lease.IsOwner)
        {
            return await ResolveExistingEnrollmentAsync(user.UserId, productHandle, subscriptionReference, cancellationToken);
        }

        try
        {
            var existingSubscription = await _client.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existingSubscription is not null)
            {
                await _store.CompleteAsync(
                    lease.Enrollment.Id,
                    lease.AttemptToken!,
                    existingSubscription.Customer.Id,
                    existingSubscription.Id,
                    cancellationToken);
                return ToSubscription(existingSubscription);
            }

            var customerReference = CustomerReference(user.UserId);
            var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
            var subscription = await _client.CreateSubscriptionAsync(
                customerReference,
                productHandle,
                subscriptionReference,
                cancellationToken);
            await _store.CompleteAsync(
                lease.Enrollment.Id,
                lease.AttemptToken!,
                customer.Id,
                subscription.Id,
                cancellationToken);
            return ToSubscription(subscription);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsRemoteFailure(exception))
        {
            var recovered = await TryRecoverSubscriptionAsync(subscriptionReference, cancellationToken);
            if (recovered is not null)
            {
                await _store.CompleteAsync(
                    lease.Enrollment.Id,
                    lease.AttemptToken!,
                    recovered.Customer.Id,
                    recovered.Id,
                    cancellationToken);
                return ToSubscription(recovered);
            }

            if (exception is MaxioApiException { StatusCode: HttpStatusCode.UnprocessableEntity })
            {
                await _store.FailAsync(lease.Enrollment.Id, lease.AttemptToken!, cancellationToken);
            }

            throw CreateUnavailableException(exception);
        }
    }

    private async Task<BillingSubscription> ResolveExistingEnrollmentAsync(
        string userId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(ConcurrentRequestWait);
        do
        {
            var remote = await _client.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (remote is not null)
            {
                return ToSubscription(remote);
            }

            var enrollment = await _store.GetAsync(userId, productHandle, cancellationToken);
            if (enrollment?.Status == SubscriptionEnrollmentStatus.Failed)
            {
                throw new BillingUnavailableException("The subscription could not be created. Retry the request.");
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        } while (true);

        throw new SubscriptionInProgressException(
            "This subscription request is already being processed. Retry shortly to receive the same result.");
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        BillingUser user,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = CustomerName(user.Email);
        try
        {
            return await _client.CreateCustomerAsync(
                customerReference,
                firstName,
                lastName,
                user.Email,
                cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var customer = await _client.FindCustomerAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                throw;
            }

            return customer;
        }
    }

    private async Task<MaxioSubscription?> TryRecoverSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _client.FindSubscriptionAsync(reference, cancellationToken);
        }
        catch (Exception recoveryException) when (!cancellationToken.IsCancellationRequested && IsRemoteFailure(recoveryException))
        {
            _logger.LogWarning(recoveryException, "Unable to recover a Maxio subscription after a failed create request.");
            return null;
        }
    }

    private BillingUnavailableException CreateUnavailableException(Exception exception)
    {
        if (exception is MaxioApiException maxioException)
        {
            _logger.LogWarning(
                exception,
                "Maxio request failed with status {StatusCode}: {Errors}",
                (int)maxioException.StatusCode,
                string.Join("; ", maxioException.Errors));
        }
        else
        {
            _logger.LogWarning(exception, "Maxio request failed.");
        }

        return new BillingUnavailableException(
            "The subscription billing service is temporarily unavailable.",
            exception);
    }

    private static bool IsRemoteFailure(Exception exception) =>
        exception is MaxioApiException or HttpRequestException or TaskCanceledException;

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new(
        product.Handle!,
        product.Name,
        product.Description,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit,
        product.RequireCreditCard);

    private static BillingSubscription ToSubscription(MaxioSubscription subscription)
    {
        var product = subscription.Product ?? throw new BillingUnavailableException(
            "Maxio returned a subscription without an associated plan.");
        return new BillingSubscription(
            subscription.Id,
            subscription.Reference,
            subscription.State,
            product.Handle ?? string.Empty,
            product.Name,
            subscription.ProductPriceInCents,
            product.Interval,
            product.IntervalUnit,
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt);
    }

    private static string CustomerReference(string userId) => $"eshop-user-{userId}";

    private static string SubscriptionReference(string userId, string productHandle)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{productHandle}"));
        return $"eshop-sub-{Convert.ToHexString(hash)[..32].ToLowerInvariant()}";
    }

    private static (string FirstName, string LastName) CustomerName(string email)
    {
        var localPart = email.Split('@', 2)[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            >= 2 => (parts[0], string.Join(' ', parts.Skip(1))),
            1 => (parts[0], "Customer"),
            _ => ("eShopOnWeb", "Customer")
        };
    }
}
