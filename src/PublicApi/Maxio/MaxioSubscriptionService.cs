using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlansEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioApiClient _client;
    private readonly AppIdentityDbContext _identityDbContext;
    private readonly MaxioSettings _settings;

    public MaxioSubscriptionService(
        MaxioApiClient client,
        AppIdentityDbContext identityDbContext,
        IOptions<MaxioSettings> settings)
    {
        _client = client;
        _identityDbContext = identityDbContext;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsAsync(cancellationToken);

        return products
            .Where(p => p.ArchivedAt == null)
            .Where(p => string.Equals(p.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Select(MapPlan)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, string email, string productHandle, CancellationToken cancellationToken = default)
    {
        var customer = await EnsureCustomerAsync(userId, email, cancellationToken);

        var reference = BuildSubscriptionReference(userId, productHandle);

        // Idempotency: if this user already has a subscription to this product,
        // return it instead of creating a duplicate (double-click safe).
        var existing = await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (existing != null)
        {
            return MapSubscription(existing);
        }

        var customerSubscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var live = customerSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            IsLiveState(s.State));
        if (live != null)
        {
            return MapSubscription(live);
        }

        try
        {
            var created = await _client.CreateSubscriptionAsync(new CreateMaxioSubscriptionRequest
            {
                Subscription = new CreateMaxioSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customer.Id,
                    Reference = reference,
                    PaymentCollectionMethod = PaymentCollectionMethodRemittance
                }
            }, cancellationToken);
            return MapSubscription(created);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request may have created the subscription first.
            var found = await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (found != null)
            {
                return MapSubscription(found);
            }
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        try
        {
            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            return subscriptions
                .OrderByDescending(s => s.CreatedAt)
                .Select(MapSubscription)
                .ToList();
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // The customer no longer exists in Maxio (e.g. sandbox re-seeded).
            return Array.Empty<SubscriptionDto>();
        }
    }

    private async Task<Customer> EnsureCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var mapping = await _identityDbContext.MaxioCustomerMappings
            .FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (mapping != null)
        {
            return new Customer
            {
                Id = mapping.MaxioCustomerId,
                Reference = mapping.MaxioCustomerReference,
                Email = email
            };
        }

        var customer = await _client.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer != null)
        {
            await SaveMappingAsync(userId, customer, cancellationToken);
            return customer;
        }

        var (firstName, lastName) = SplitName(email);

        try
        {
            customer = await _client.CreateCustomerAsync(new CreateMaxioCustomerRequest
            {
                Customer = new CreateMaxioCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = userId
                }
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request created the customer first (reference is unique).
            customer = await _client.FindCustomerByReferenceAsync(userId, cancellationToken);
            if (customer == null)
            {
                throw;
            }
        }

        await SaveMappingAsync(userId, customer, cancellationToken);
        return customer;
    }

    private async Task SaveMappingAsync(string userId, Customer customer, CancellationToken cancellationToken)
    {
        var existing = await _identityDbContext.MaxioCustomerMappings
            .FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (existing != null)
        {
            existing.MaxioCustomerId = customer.Id;
            existing.MaxioCustomerReference = customer.Reference ?? userId;
            existing.CreatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            _identityDbContext.MaxioCustomerMappings.Add(new MaxioCustomerMapping
            {
                UserId = userId,
                MaxioCustomerId = customer.Id,
                MaxioCustomerReference = customer.Reference ?? userId,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await _identityDbContext.SaveChangesAsync(cancellationToken);
    }

    private static string BuildSubscriptionReference(string userId, string productHandle) => $"{userId}:{productHandle}";

    private static bool IsLiveState(string? state) => state switch
    {
        "active" or "trialing" or "assessing" or "pending" or "past_due" or
        "soft_failure" or "suspended" or "unpaid" or "trial_ended" or
        "on_hold" or "awaiting_signup" => true,
        _ => false
    };

    private static (string FirstName, string LastName) SplitName(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return ("eShopOnWeb", "Shopper");
        }
        if (parts.Length == 1)
        {
            return (parts[0], "Shopper");
        }
        return (parts[0], string.Join(" ", parts.Skip(1)));
    }

    private static SubscriptionPlanDto MapPlan(Product product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        Currency = "USD",
        Taxable = product.Taxable,
        RequireCreditCard = product.RequireCreditCard,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty
    };

    private static SubscriptionDto MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        Reference = subscription.Reference,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit,
        Currency = subscription.Currency,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CustomerId = subscription.Customer?.Id,
        CustomerReference = subscription.Customer?.Reference
    };
}
