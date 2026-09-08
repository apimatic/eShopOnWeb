using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of the subscription billing capability.
/// The eShopOnWeb user id is used as the Maxio customer "reference", giving an
/// idempotent, restart-safe mapping between shoppers and billing customers without
/// a local database (Maxio is the system of record).
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    private const int PerPage = 100;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly MaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioApiClient client, IOptions<MaxioOptions> options, IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await FindProductFamilyByHandleAsync(_options.ProductFamilyHandle, cancellationToken);
        if (family == null)
        {
            throw new InvalidOperationException(
                $"Maxio product family with handle '{_options.ProductFamilyHandle}' was not found. Check the 'Maxio:ProductFamilyHandle' setting (MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }

        var products = new List<MaxioProduct>();
        await _client.ScanPagedAsync<MaxioProductResponse>(
            $"product_families/{family.Id}/products.json",
            PerPage,
            page =>
            {
                foreach (var envelope in page)
                {
                    if (envelope.Product != null)
                    {
                        products.Add(envelope.Product);
                    }
                }
                return false;
            },
            cancellationToken);

        var plans = products
            .Where(p => p.ArchivedAt == null)
            .Select(MapPlan)
            .OrderBy(p => p.Price)
            .ToList();

        _logger.LogInformation($"Loaded {plans.Count} subscription plan(s) from Maxio product family '{_options.ProductFamilyHandle}'.");
        return plans;
    }

    public async Task<UserSubscription> SubscribeAsync(BillingUserInfo user, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new InvalidOperationException("A plan handle is required to subscribe.");
        }

        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan == null)
        {
            throw new InvalidOperationException($"Plan '{planHandle}' is not available for subscription.");
        }

        var userLock = UserLocks.GetOrAdd(user.UserId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(user, cancellationToken);

            var existingSubscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = existingSubscriptions.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                MaxioSubscriptionStates.LiveStates.Contains(s.State));
            if (existing != null)
            {
                _logger.LogInformation($"User {user.UserId} already holds live subscription {existing.Id} for plan '{plan.Handle}'; returning it unchanged.");
                return MapSubscription(existing);
            }

            var request = new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id
                }
            };
            var created = await _client.PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>(
                "subscriptions.json", request, cancellationToken);

            _logger.LogInformation($"Created Maxio subscription {created.Subscription?.Id} (plan '{plan.Handle}') for user {user.UserId} / customer {customer.Id}.");
            return MapSubscription(created.Subscription!);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<UserSubscription>> GetSubscriptionsForUserAsync(BillingUserInfo user, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(user.UserId, cancellationToken);
        if (customer == null)
        {
            return new List<UserSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(BillingUserInfo user, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(user.UserId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveNames(user);
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = user.Email,
                Reference = user.UserId
            }
        };

        MaxioCustomerResponse created;
        try
        {
            created = await _client.PostAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>("customers.json", request, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Lost a create race: the reference is now taken, so the customer exists.
            existing = await FindCustomerByReferenceAsync(user.UserId, cancellationToken);
            if (existing != null)
            {
                return existing;
            }
            throw;
        }

        _logger.LogInformation($"Created Maxio customer {created.Customer?.Id} for user {user.UserId}.");
        return created.Customer!;
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await _client.GetAsync<MaxioCustomerResponse>(path, cancellationToken);
        return response?.Customer;
    }

    private async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var subscriptions = new List<MaxioSubscription>();
        await _client.ScanPagedAsync<MaxioSubscriptionResponse>(
            $"customers/{customerId}/subscriptions.json",
            PerPage,
            page =>
            {
                foreach (var envelope in page)
                {
                    if (envelope.Subscription != null)
                    {
                        subscriptions.Add(envelope.Subscription);
                    }
                }
                return false;
            },
            cancellationToken);
        return subscriptions;
    }

    private async Task<MaxioProductFamily?> FindProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        MaxioProductFamily? found = null;
        await _client.ScanPagedAsync<MaxioProductFamilyResponse>(
            "product_families.json",
            PerPage,
            page =>
            {
                foreach (var envelope in page)
                {
                    if (envelope.ProductFamily != null &&
                        string.Equals(envelope.ProductFamily.Handle, handle, StringComparison.OrdinalIgnoreCase))
                    {
                        found = envelope.ProductFamily;
                        return true;
                    }
                }
                return false;
            },
            cancellationToken);
        return found;
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        ProductId = product.Id,
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty
    };

    private static UserSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        Price = (subscription.Product?.PriceInCents ?? subscription.ProductPriceInCents) / 100m,
        State = subscription.State,
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt ?? DateTimeOffset.MinValue,
        IsActive = MaxioSubscriptionStates.LiveStates.Contains(subscription.State)
    };

    private static (string FirstName, string LastName) DeriveNames(BillingUserInfo user)
    {
        var source = !string.IsNullOrWhiteSpace(user.UserName) ? user.UserName : user.Email;
        var localPart = source.Contains('@') ? source.Split('@')[0] : source;
        var separators = new[] { '.', '-', '_', ' ' };
        var parts = localPart.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return (parts[0], parts[1]);
        }
        if (parts.Length == 1 && parts[0].Length > 0)
        {
            return (parts[0], "Customer");
        }
        return ("eShopOnWeb", "Customer");
    }
}
