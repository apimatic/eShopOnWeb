using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class SubscriptionService : ISubscriptionService
{
    // Invoice (remittance) collection means no payment method is required at signup, which
    // matches the seeded plans ("payment method not required").
    private const string PaymentCollectionMethod = "remittance";

    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "past_due", "unpaid", "on_hold", "suspended",
        "trial_ended", "pending_cancellation", "assessing", "pending", "awaiting_signup"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly IRepository<MaxioCustomer> _customerRepository;
    private readonly IRepository<MaxioSubscription> _subscriptionRepository;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioClient maxioClient,
        IRepository<MaxioCustomer> customerRepository,
        IRepository<MaxioSubscription> subscriptionRepository,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _customerRepository = customerRepository;
        _subscriptionRepository = subscriptionRepository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle!,
                Name = string.IsNullOrWhiteSpace(p.Name) ? p.Handle! : p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Price = p.PriceInCents / 100m,
                Interval = p.Interval,
                IntervalUnit = string.IsNullOrWhiteSpace(p.IntervalUnit) ? "month" : p.IntervalUnit,
                RequiresPaymentMethod = p.RequireCreditCard
            })
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionResult> SubscribeAsync(string userKey, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userKey))
        {
            throw new ArgumentException("A user key is required.", nameof(userKey));
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var customer = await EnsureCustomerAsync(userKey, email, cancellationToken);
        _logger.LogInformation("Subscribe: user {UserKey} plan {PlanHandle} -> Maxio customer {CustomerId}.", userKey, planHandle, customer.MaxioCustomerId);

        // Fast path: a local mapping already exists for this user + plan.
        var mapping = await _subscriptionRepository.FirstOrDefaultAsync(
            new MaxioSubscriptionByUserAndPlanSpec(userKey, planHandle), cancellationToken);
        if (mapping is not null)
        {
            var current = await _maxioClient.GetSubscriptionAsync(mapping.MaxioSubscriptionId, cancellationToken);
            if (IsLive(current.State))
            {
                return Map(current, isNew: false);
            }
        }

        // Idempotency: if the user already has a live subscription to this plan, return it.
        var existing = await FindLiveSubscriptionAsync(customer.MaxioCustomerId, planHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Subscribe: user {UserKey} already has live subscription {SubscriptionId} to plan {PlanHandle}; returning it.",
                userKey, existing.Id, planHandle);
            await SaveSubscriptionMappingAsync(userKey, customer.MaxioCustomerId, existing, cancellationToken);
            return Map(existing, isNew: false);
        }

        // Create the subscription. A deterministic uniqueness token makes a double-click
        // safe: the second request is rejected by Maxio with 409 and we return the
        // subscription created by the first request.
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionBody
            {
                ProductHandle = planHandle,
                CustomerId = customer.MaxioCustomerId,
                PaymentCollectionMethod = PaymentCollectionMethod
            },
            UniquenessToken = CreateUniquenessToken(userKey, planHandle)
        };

        MaxioSubscriptionDto created;
        try
        {
            created = await _maxioClient.CreateSubscriptionAsync(request, cancellationToken);
            _logger.LogInformation("Subscribe: created Maxio subscription {SubscriptionId} for user {UserKey} plan {PlanHandle}.",
                created.Id, userKey, planHandle);
        }
        catch (MaxioApiException ex) when (ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request already created the subscription (409), or Maxio
            // rejected the signup (422). Re-check for a live subscription to the plan.
            _logger.LogInformation("Subscribe: create returned {StatusCode} for user {UserKey} plan {PlanHandle}; re-checking for an existing subscription.",
                (int)ex.StatusCode, userKey, planHandle);
            var duplicate = await FindLiveSubscriptionAsync(customer.MaxioCustomerId, planHandle, cancellationToken);
            if (duplicate is null)
            {
                throw;
            }
            _logger.LogInformation("Subscription already existed for user {UserKey} plan {PlanHandle}; returning existing subscription {SubscriptionId}.",
                userKey, planHandle, duplicate.Id);
            await SaveSubscriptionMappingAsync(userKey, customer.MaxioCustomerId, duplicate, cancellationToken);
            return Map(duplicate, isNew: false);
        }

        await SaveSubscriptionMappingAsync(userKey, customer.MaxioCustomerId, created, cancellationToken);
        return Map(created, isNew: true);
    }

    public async Task<IReadOnlyList<SubscriptionResult>> GetMySubscriptionsAsync(string userKey, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerAsync(userKey, cancellationToken);
        if (customer is null)
        {
            return new List<SubscriptionResult>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.MaxioCustomerId, cancellationToken);
        return subscriptions.Select(s => Map(s, isNew: false)).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userKey, string email, CancellationToken cancellationToken)
    {
        var local = await _customerRepository.FirstOrDefaultAsync(new MaxioCustomerByUserSpec(userKey), cancellationToken);
        if (local is not null)
        {
            return local;
        }

        var existing = await _maxioClient.GetCustomerByReferenceAsync(userKey, cancellationToken);
        if (existing is not null)
        {
            return await SaveCustomerMappingAsync(userKey, existing, cancellationToken);
        }

        try
        {
            var created = await _maxioClient.CreateCustomerAsync(new CreateCustomerRequest
            {
                Customer = new MaxioCustomerDto
                {
                    FirstName = DeriveFirstName(email),
                    LastName = DeriveLastName(email),
                    Email = email,
                    Reference = userKey
                }
            }, cancellationToken);
            return await SaveCustomerMappingAsync(userKey, created, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Another concurrent request created the customer with the same reference.
            var duplicate = await _maxioClient.GetCustomerByReferenceAsync(userKey, cancellationToken);
            if (duplicate is null)
            {
                throw;
            }
            return await SaveCustomerMappingAsync(userKey, duplicate, cancellationToken);
        }
    }

    private async Task<MaxioCustomer?> FindCustomerAsync(string userKey, CancellationToken cancellationToken)
    {
        var local = await _customerRepository.FirstOrDefaultAsync(new MaxioCustomerByUserSpec(userKey), cancellationToken);
        if (local is not null)
        {
            return local;
        }

        var existing = await _maxioClient.GetCustomerByReferenceAsync(userKey, cancellationToken);
        if (existing is not null)
        {
            return await SaveCustomerMappingAsync(userKey, existing, cancellationToken);
        }

        return null;
    }

    private async Task<MaxioCustomer> SaveCustomerMappingAsync(string userKey, MaxioCustomerDto customer, CancellationToken cancellationToken)
    {
        var entity = new MaxioCustomer
        {
            UserId = userKey,
            MaxioCustomerId = customer.Id,
            Reference = customer.Reference ?? userKey,
            CreatedAt = DateTime.UtcNow
        };
        await _customerRepository.AddAsync(entity, cancellationToken);
        return entity;
    }

    private async Task SaveSubscriptionMappingAsync(string userKey, int customerId, MaxioSubscriptionDto subscription, CancellationToken cancellationToken)
    {
        var existing = await _subscriptionRepository.FirstOrDefaultAsync(
            new MaxioSubscriptionByUserAndPlanSpec(userKey, subscription.Product?.Handle ?? string.Empty), cancellationToken);
        if (existing is not null)
        {
            existing.MaxioSubscriptionId = subscription.Id;
            existing.MaxioCustomerId = customerId;
            existing.State = subscription.State ?? string.Empty;
            await _subscriptionRepository.UpdateAsync(existing, cancellationToken);
            return;
        }

        var entity = new MaxioSubscription
        {
            UserId = userKey,
            MaxioCustomerId = customerId,
            MaxioSubscriptionId = subscription.Id,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            State = subscription.State ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };
        await _subscriptionRepository.AddAsync(entity, cancellationToken);
    }

    private async Task<MaxioSubscriptionDto?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) && IsLive(s.State));
    }

    private static bool IsLive(string? state)
    {
        return !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state);
    }

    private static SubscriptionResult Map(MaxioSubscriptionDto subscription, bool isNew)
    {
        return new SubscriptionResult
        {
            SubscriptionId = subscription.Id,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            Price = subscription.ProductPriceInCents / 100m,
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod ?? string.Empty,
            IsNew = isNew
        };
    }

    private static string CreateUniquenessToken(string userKey, string planHandle)
    {
        using var md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes($"eshop-subscribe:{userKey}:{planHandle}"));
        return new Guid(hash).ToString();
    }

    private static string DeriveFirstName(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "eShop";
        }
        int at = email.IndexOf('@');
        string local = at > 0 ? email[..at] : email;
        return string.IsNullOrWhiteSpace(local) ? "eShop" : local;
    }

    private static string DeriveLastName(string email)
    {
        return "User";
    }
}
