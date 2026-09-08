using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing as the system of
/// record. A shopper is mapped to a Maxio customer via the <c>reference</c> field, which makes
/// customer/subscription creation idempotent even though this app has no durable local storage.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Subscription states that mean "the customer is currently enrolled in this plan". When one
    // exists for the requested plan the signup is treated as idempotent and the existing
    // subscription is returned instead of creating a duplicate.
    private static readonly HashSet<string> EnrolledStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "past_due", "unpaid"
    };

    // Serializes the create path per (customer, plan) so a double-click can never race two
    // subscriptions into existence on a single host.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SignupLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly MaxioBillingApiClient _client;
    private readonly IOptions<MaxioBillingOptions> _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(MaxioBillingApiClient client, IOptions<MaxioBillingOptions> options, ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return await RunAsync(async ct =>
        {
            string familyHandle = ProductFamilyHandleOrThrow();

            var family = await _client.FindProductFamilyByHandleAsync(familyHandle, ct).ConfigureAwait(false)
                ?? throw new SubscriptionBillingException(
                    $"No Maxio product family was found for handle '{familyHandle}'. Check the Maxio:ProductFamilyHandle setting.", 500);

            var products = await _client.ListProductsForFamilyAsync(family.Id, ct).ConfigureAwait(false);

            return products
                .Where(product => product.ArchivedAt == null)
                .Select(ToSubscriptionPlan)
                .OrderBy(plan => plan.Price)
                .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubscriptionSignupResult> SubscribeAsync(SubscriptionCustomer customer, string planHandle, CancellationToken cancellationToken = default)
    {
        return await RunAsync(async ct =>
        {
            if (customer == null) throw new ArgumentNullException(nameof(customer));
            if (string.IsNullOrWhiteSpace(customer.Reference))
            {
                throw new SubscriptionBillingException("A customer reference is required.", 400);
            }
            if (string.IsNullOrWhiteSpace(planHandle))
            {
                throw new SubscriptionBillingException("A plan handle is required.", 400);
            }

            var plans = await ListPlansAsync(ct).ConfigureAwait(false);
            if (plans.All(plan => !plan.Handle.Equals(planHandle, StringComparison.OrdinalIgnoreCase)))
            {
                return new SubscriptionSignupResult { Status = SubscriptionSignupStatus.PlanNotFound };
            }

            var maxioCustomer = await EnsureCustomerAsync(customer, ct).ConfigureAwait(false);

            var existing = await FindEnrolledSubscriptionAsync(maxioCustomer.Id, planHandle, ct).ConfigureAwait(false);
            if (existing != null)
            {
                return NewResult(SubscriptionSignupStatus.AlreadySubscribed, maxioCustomer.Id, existing);
            }

            var gate = SignupLocks.GetOrAdd($"{customer.Reference}|{planHandle}", _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Re-check now that we hold the per-key lock: a concurrent twin request may have
                // already created the subscription between our earlier check and acquiring the lock.
                existing = await FindEnrolledSubscriptionAsync(maxioCustomer.Id, planHandle, ct).ConfigureAwait(false);
                if (existing != null)
                {
                    return NewResult(SubscriptionSignupStatus.AlreadySubscribed, maxioCustomer.Id, existing);
                }

                var attributes = new CreateSubscriptionAttributes
                {
                    ProductHandle = planHandle,
                    CustomerReference = customer.Reference
                };

                try
                {
                    var created = await _client.CreateSubscriptionAsync(attributes, Guid.NewGuid().ToString("N"), ct).ConfigureAwait(false);
                    return NewResult(SubscriptionSignupStatus.Created, maxioCustomer.Id, ToCustomerSubscription(created));
                }
                catch (MaxioApiException apiException) when (apiException.StatusCode == 409)
                {
                    // A uniqueness-token collision usually means a twin request already succeeded
                    // (e.g. an out-of-process race). Recover by returning that subscription.
                    existing = await FindEnrolledSubscriptionAsync(maxioCustomer.Id, planHandle, ct).ConfigureAwait(false);
                    if (existing != null)
                    {
                        return NewResult(SubscriptionSignupStatus.AlreadySubscribed, maxioCustomer.Id, existing);
                    }
                    throw;
                }
            }
            finally
            {
                gate.Release();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        return await RunAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(customerReference))
            {
                throw new SubscriptionBillingException("A customer reference is required.", 400);
            }

            var customer = await _client.FindCustomerByReferenceAsync(customerReference, ct).ConfigureAwait(false);
            if (customer == null)
            {
                // The shopper never subscribed; there is nothing to list yet.
                return (IReadOnlyList<CustomerSubscription>)Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, ct).ConfigureAwait(false);
            return subscriptions
                .OrderByDescending(subscription => subscription.CreatedAt)
                .Select(ToCustomerSubscription)
                .ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriptionCustomer customer, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(customer.Reference, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            return await _client.CreateCustomerAsync(new CustomerAttributes
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Organization = "eShopOnWeb",
                Reference = customer.Reference
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MaxioApiException apiException) when (apiException.StatusCode == 422)
        {
            // A concurrent request created the customer between our lookup and create. Reuse it.
            var created = await _client.FindCustomerByReferenceAsync(customer.Reference, cancellationToken).ConfigureAwait(false);
            if (created != null)
            {
                return created;
            }
            throw;
        }
    }

    private async Task<CustomerSubscription?> FindEnrolledSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken).ConfigureAwait(false);

        var enrolled = subscriptions.FirstOrDefault(subscription =>
            EnrolledStates.Contains(subscription.State) &&
            subscription.Product != null &&
            subscription.Product.Handle.Equals(planHandle, StringComparison.OrdinalIgnoreCase));

        return enrolled == null ? null : ToCustomerSubscription(enrolled);
    }

    private string ProductFamilyHandleOrThrow()
    {
        string? handle = _options.Value.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new SubscriptionBillingException(
                "The subscription catalog is not configured. Set the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable (Maxio:ProductFamilyHandle).", 500);
        }
        return handle;
    }

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        catch (MaxioBillingConfigurationException configurationException)
        {
            throw new SubscriptionBillingException(configurationException.Message, 500);
        }
        catch (MaxioApiException apiException)
        {
            throw Translate(apiException);
        }
    }

    private SubscriptionBillingException Translate(MaxioApiException apiException)
    {
        if (apiException.StatusCode >= 400 && apiException.StatusCode < 500)
        {
            return new SubscriptionBillingException(apiException.Message, apiException.StatusCode);
        }

        _logger.LogError(apiException, "Maxio billing request failed with status {Status}.", apiException.StatusCode);
        return new SubscriptionBillingException("The billing provider could not complete the request.", 502);
    }

    private static SubscriptionSignupResult NewResult(SubscriptionSignupStatus status, long customerId, CustomerSubscription? subscription)
    {
        return new SubscriptionSignupResult
        {
            Status = status,
            CustomerId = customerId,
            Subscription = subscription
        };
    }

    private static SubscriptionPlan ToSubscriptionPlan(MaxioProduct product)
    {
        return new SubscriptionPlan
        {
            ProductId = product.Id,
            Handle = product.Handle,
            Name = product.Name,
            Description = product.Description,
            Price = CentsToPrice(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit
        };
    }

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription)
    {
        var product = subscription.Product;
        return new CustomerSubscription
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State,
            ProductHandle = product?.Handle ?? string.Empty,
            ProductName = product?.Name ?? string.Empty,
            Price = CentsToPrice(product?.PriceInCents ?? subscription.ProductPriceInCents),
            Interval = product?.Interval ?? 1,
            IntervalUnit = product?.IntervalUnit ?? string.Empty,
            Currency = subscription.Currency ?? string.Empty,
            Balance = CentsToPrice(subscription.BalanceInCents),
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
        };
    }

    private static decimal CentsToPrice(int cents) => cents / 100m;
}
