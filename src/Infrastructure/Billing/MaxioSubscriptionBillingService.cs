using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing (system of record). A Maxio customer is
/// keyed on the eShopOnWeb user id via the customer "reference", so no local mapping is needed.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int PageSize = 200;

    // Serialises subscribe per user within this process so a double-click cannot create duplicates.
    private static readonly SemaphoreSlim[] UserLocks =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            var plans = await FetchPlansAsync(cancellationToken);
            return (IReadOnlyList<SubscriptionPlan>)plans.Select(ToPlan).OrderBy(p => p.Price).ToList();
        });

    public Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle,
        CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            var gate = UserLocks[(uint)subscriber.UserId.GetHashCode() % (uint)UserLocks.Length];
            await gate.WaitAsync(cancellationToken);
            try
            {
                var plan = (await FetchPlansAsync(cancellationToken))
                    .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
                    ?? throw new SubscriptionPlanNotFoundException(planHandle);

                if (plan.RequireCreditCard == true)
                    throw new BillingRejectedException(
                        $"Plan '{plan.Handle}' requires a payment method, which this API does not collect.");

                var customer = await GetOrCreateCustomerAsync(subscriber, cancellationToken);

                var existing = (await ListCustomerSubscriptionsAsync(customer.Id!.Value, cancellationToken))
                    .FirstOrDefault(s => IsLive(s) &&
                        string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    _logger.LogInformation("User {UserId} already subscribed to {Plan} ({SubscriptionId})",
                        subscriber.UserId, plan.Handle, existing.Id);
                    return new SubscribeResult(ToSubscription(existing), false);
                }

                var response = await _client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionOperationRequest
                    {
                        Body = new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = plan.Handle,
                                CustomerId = customer.Id,
                                // No card is collected here: bill by invoice (plans do not require a payment method).
                                PaymentCollectionMethod = CollectionMethod.Remittance,
                            }
                        }
                    }, cancellationToken: cancellationToken);

                var created = response.Subscription
                    ?? throw new BillingUnavailableException("Billing returned an empty subscription response.");
                _logger.LogInformation("Created subscription {SubscriptionId} for user {UserId} on {Plan}",
                    created.Id, subscriber.UserId, plan.Handle);
                return new SubscribeResult(ToSubscription(created), true);
            }
            finally
            {
                gate.Release();
            }
        });

    public Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(Subscriber subscriber,
        CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            var customer = await FindCustomerAsync(subscriber.UserId, cancellationToken);
            if (customer is null)
                return (IReadOnlyList<CustomerSubscription>)Array.Empty<CustomerSubscription>();

            var subs = await ListCustomerSubscriptionsAsync(customer.Id!.Value, cancellationToken);
            return (IReadOnlyList<CustomerSubscription>)subs.Select(ToSubscription)
                .OrderByDescending(s => s.CreatedAt).ToList();
        });

    private async Task<List<Product>> FetchPlansAsync(CancellationToken ct)
    {
        MaxioSettingsValidator.EnsureConfigured(_settings);
        var all = new List<Product>();
        for (var page = 1; ; page++)
        {
            var batch = await _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = $"handle:{_settings.ProductFamilyHandle}",
                    Page = page,
                    PerPage = PageSize,
                    IncludeArchived = false,
                }, cancellationToken: ct);
            all.AddRange(batch.Select(b => b.Product).Where(p => p.Handle is not null && p.ArchivedAt is null));
            if (batch.Count < PageSize) return all;
        }
    }

    private async Task<Customer?> FindCustomerAsync(string userId, CancellationToken ct)
    {
        MaxioSettingsValidator.EnsureConfigured(_settings);
        try
        {
            var found = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = userId }, cancellationToken: ct);
            return found.Customer;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Customer> GetOrCreateCustomerAsync(Subscriber subscriber, CancellationToken ct)
    {
        var existing = await FindCustomerAsync(subscriber.UserId, ct);
        if (existing is not null) return existing;

        var localPart = subscriber.Email.Split('@')[0];
        var created = await _client.Customers.CreateCustomer(
            new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = localPart,
                        LastName = "eShopOnWeb",
                        Email = subscriber.Email,
                        Reference = subscriber.UserId,
                    }
                }
            }, cancellationToken: ct);
        _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", created.Customer.Id, subscriber.UserId);
        return created.Customer;
    }

    private async Task<List<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        var subs = await _client.Customers.ListCustomerSubscriptions(
            new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct);
        return subs.Select(s => s.Subscription).Where(s => s is not null).Select(s => s!)
            .Where(s => s.Product?.ProductFamily?.Handle is null ||
                        string.Equals(s.Product.ProductFamily.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static bool IsLive(Subscription s) =>
        s.State is not null &&
        !s.State.Is("canceled") && !s.State.Is("expired") && !s.State.Is("failed_to_create");

    private static SubscriptionPlan ToPlan(Product p) => new(
        p.Handle!, p.Name ?? p.Handle!, p.Description,
        (p.PriceInCents ?? 0) / 100m,
        p.Interval ?? 1, p.IntervalUnit?.Value ?? "month",
        p.RequireCreditCard == true);

    private static CustomerSubscription ToSubscription(Subscription s) => new(
        s.Id?.ToString() ?? string.Empty,
        s.Product?.Handle ?? string.Empty,
        s.Product?.Name ?? string.Empty,
        (s.ProductPriceInCents ?? s.Product?.PriceInCents ?? 0) / 100m,
        s.Currency,
        s.State?.Value ?? "unknown",
        s.NextAssessmentAt,
        s.CreatedAt);

    /// <summary>Translates SDK/transport failures into the billing exceptions the API layer understands.</summary>
    private async Task<T> Guard<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (ApiException<CreateSubscriptionError> ex) when ((int)ex.StatusCode == 422)
        {
            var details = ex.Error.TryGetErrorListResponse1(out var list) ? string.Join("; ", list.Errors) : "request rejected";
            _logger.LogWarning("Maxio rejected subscription: {Details}", details);
            throw new BillingRejectedException($"Billing rejected the subscription: {details}", ex);
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 422)
        {
            _logger.LogWarning(ex, "Maxio rejected the request (422)");
            throw new BillingRejectedException("Billing rejected the request.", ex);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Maxio call failed with HTTP {Status}", (int)ex.StatusCode);
            throw new BillingUnavailableException($"Billing system returned HTTP {(int)ex.StatusCode}.", ex);
        }
        catch (SdkException ex)
        {
            _logger.LogError(ex, "Maxio SDK failure");
            throw new BillingUnavailableException("Billing system call failed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
        {
            _logger.LogError(ex, "Maxio unreachable or timed out");
            throw new BillingUnavailableException("Billing system is unreachable.", ex);
        }
    }
}
