using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
/// The eShopOnWeb username is the Maxio customer <c>reference</c> and the idempotency key:
/// a customer is created only when the reference lookup misses, and a subscription is created
/// only when the customer has no live subscription for the same plan.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    // Serializes subscribe calls per user so a double-click (or concurrent retry) cannot
    // race the ensure-customer / ensure-subscription checks into duplicate creates.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userGates = new();

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        return await Bounded(async ct =>
        {
            try
            {
                var plans = new List<SubscriptionPlan>();
                var page = 1;
                const int perPage = 200;
                while (true)
                {
                    var products = await _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: "handle:" + _settings.ProductFamilyHandle,
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: page,
                        perPage: perPage,
                        ct: ct);

                    plans.AddRange(products.Select(p => MapPlan(p.Product)));
                    if (products.Count < perPage)
                    {
                        return (IReadOnlyList<SubscriptionPlan>)plans;
                    }
                    page++;
                }
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var message))
                {
                    // 404 — the configured product family does not exist on this site: a
                    // server-side configuration problem, not something the caller can fix.
                    _logger.LogError("Maxio product family '{Handle}' not found: {Message}", _settings.ProductFamilyHandle, message);
                    throw new BillingException(502, "The billing catalog is not configured correctly.", ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw TranslateRawError(raw, ex);
                }
                throw new BillingException(502, "The billing provider rejected the request.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw ProviderUnreachable(ex);
            }
            catch (JsonException ex)
            {
                throw ProviderResponseUnreadable(ex);
            }
        }, cancellationToken);
    }

    public async Task<CustomerSubscription> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException(400, "A product handle is required.");
        }

        var gate = _userGates.GetOrAdd(username, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await Bounded(async ct =>
            {
                var customer = await EnsureCustomerAsync(username, ct);
                if (customer.Id is not int customerId)
                {
                    throw new BillingException(502, "The billing provider returned a customer without an id.");
                }

                var existing = await ListCustomerSubscriptionsAsync(customerId, ct);
                var current = existing.FirstOrDefault(s =>
                    s.Product?.Handle == productHandle && IsLive(s.State));
                if (current is not null)
                {
                    return MapSubscription(current);
                }

                try
                {
                    var response = await _client.Subscriptions.CreateSubscription(
                        new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = productHandle,
                                CustomerId = customerId,
                                // Invoice collection: the balance is billed by invoice, so signup
                                // succeeds without a card on file (no card capture / 3-DS).
                                PaymentCollectionMethod = CollectionMethod.Invoice
                            }
                        }, ct: ct);

                    if (response.Subscription is null)
                    {
                        throw new BillingException(502, "The billing provider returned an empty subscription.");
                    }
                    return MapSubscription(response.Subscription);
                }
                catch (SdkException<CreateSubscriptionError> ex)
                {
                    if (ex.Error.TryGetErrorListResponse1(out var errorList))
                    {
                        // 422 — the provider rejected the signup; its reasons are actionable.
                        var reasons = string.Join("; ", errorList.Errors);
                        throw new BillingException(422, $"The subscription was rejected: {reasons}", ex);
                    }
                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw TranslateRawError(raw, ex);
                    }
                    throw new BillingException(502, "The billing provider rejected the request.", ex);
                }
            }, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(string username, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        return await Bounded(async ct =>
        {
            var customer = await FindCustomerByReferenceAsync(username, ct);
            if (customer is null)
            {
                return (IReadOnlyList<CustomerSubscription>)Array.Empty<CustomerSubscription>();
            }
            if (customer.Id is not int customerId)
            {
                throw new BillingException(502, "The billing provider returned a customer without an id.");
            }

            var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
            return (IReadOnlyList<CustomerSubscription>)subscriptions
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!))
                .ToList();
        }, cancellationToken);
    }

    private async Task<Customer> EnsureCustomerAsync(string username, CancellationToken ct)
    {
        var existing = await FindCustomerByReferenceAsync(username, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = FirstNameOf(username),
                        LastName = LastNameOf(username),
                        Email = username,
                        Reference = username
                    }
                }, ct: ct);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A concurrent subscribe may have created the customer first (duplicate reference
            // surfaces as 422). Re-read once before treating the create as failed.
            var winner = await FindCustomerByReferenceAsync(username, ct);
            if (winner is not null)
            {
                return winner;
            }

            string detail;
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // 422. The typed model only carries per_page/price_point — real field-level
                // messages are unmodeled and dropped on deserialize, and TryGetRawError does
                // not fire for a status with a typed accessor, so the detail is lost here.
                detail = string.Empty;
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, ex);
            }
            else
            {
                detail = string.Empty;
            }

            detail = string.IsNullOrWhiteSpace(detail)
                ? "The billing provider rejected the customer record."
                : $"The billing provider rejected the customer record: {detail}";
            throw new BillingException(422, detail, ex);
        }
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string username, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(username, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw ProviderUnreachable(ex);
        }
        catch (JsonException ex)
        {
            // An unreadable lookup body is not a miss — never let it gate a create.
            throw ProviderResponseUnreadable(ex);
        }
    }

    private async Task<IReadOnlyList<Subscription?>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
            return response.Select(r => r.Subscription).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw ProviderUnreachable(ex);
        }
        catch (JsonException ex)
        {
            throw ProviderResponseUnreadable(ex);
        }
    }

    private static bool IsLive(SubscriptionState? state)
    {
        // Anything not terminal counts as "already subscribed" for idempotency.
        return state is not null
            && state != SubscriptionState.Canceled
            && state != SubscriptionState.Expired
            && state != SubscriptionState.FailedToCreate;
    }

    private static SubscriptionPlan MapPlan(Product product)
    {
        return new SubscriptionPlan
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Price = (product.PriceInCents ?? 0) / 100m,
            Interval = product.Interval ?? 0,
            IntervalUnit = product.IntervalUnit?.Value ?? string.Empty
        };
    }

    private static CustomerSubscription MapSubscription(Subscription subscription)
    {
        return new CustomerSubscription
        {
            SubscriptionId = subscription.Id ?? 0,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            State = subscription.State?.Value ?? string.Empty,
            Price = (subscription.ProductPriceInCents ?? 0) / 100m,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
        };
    }

    private static string FirstNameOf(string username)
    {
        var at = username.IndexOf('@');
        return at > 0 ? username[..at] : username;
    }

    private static string LastNameOf(string username)
    {
        var at = username.IndexOf('@');
        return at > 0 && at < username.Length - 1 ? username[(at + 1)..] : "Customer";
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BillingException(500, "Billing is not configured: Maxio:ApiKey is missing.");
        }
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl) && string.IsNullOrWhiteSpace(_settings.Subdomain))
        {
            throw new BillingException(500, "Billing is not configured: Maxio:Subdomain (or Maxio:BaseUrl) is missing.");
        }
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new BillingException(500, "Billing is not configured: Maxio:ProductFamilyHandle is missing.");
        }
    }

    private static BillingException TranslateRawError(RawError raw, Exception inner)
    {
        var status = (int)raw.StatusCode;
        if (status is >= 400 and < 500)
        {
            return new BillingException(status, $"The billing provider rejected the request (HTTP {status}).", inner);
        }
        return new BillingException(502, "The billing provider is unavailable or returned an error.", inner);
    }

    private static BillingException ProviderUnreachable(Exception ex)
    {
        return new BillingException(503, "The billing provider could not be reached.", ex);
    }

    private static BillingException ProviderResponseUnreadable(JsonException ex)
    {
        return new BillingException(502, "The billing provider returned a response that could not be processed.", ex);
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }
}
