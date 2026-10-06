using System.Net;
using Maxio.Configuration;
using Maxio.Exceptions;
using Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maxio.Services;

/// <summary>
/// Orchestrates the subscription lifecycle against Maxio Advanced Billing on behalf of an eShopOnWeb user.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private const string CustomerReferencePrefix = "eshop:";

    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(IMaxioClient maxioClient, IOptions<MaxioOptions> options, ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Product>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _options.Value.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured. Set MAXIO_DEFAULT_PRODUCT_FAMILY.");
        }

        var products = await _maxioClient.ListProductsForProductFamilyAsync($"handle:{familyHandle}", cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<Subscription> SubscribeAsync(string userEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            throw new ArgumentException("A user email is required to subscribe.", nameof(userEmail));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required to subscribe.", nameof(planHandle));
        }

        var reference = BuildCustomerReference(userEmail);
        var customer = await EnsureCustomerAsync(userEmail, reference, cancellationToken);

        var existing = await FindExistingSubscriptionAsync(customer.Id, planHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("User {UserEmail} already has subscription {SubscriptionId} for plan {PlanHandle}; returning existing subscription.",
                userEmail, existing.Id, planHandle);
            return existing;
        }

        var subscription = await _maxioClient.CreateSubscriptionAsync(new CreateSubscription
        {
            ProductHandle = planHandle,
            CustomerId = customer.Id,
            PaymentCollectionMethod = "remittance"
        }, cancellationToken);

        _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserEmail} on plan {PlanHandle}.",
            subscription.Id, userEmail, planHandle);

        return subscription;
    }

    public async Task<IReadOnlyList<Subscription>> GetMySubscriptionsAsync(string userEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            throw new ArgumentException("A user email is required.", nameof(userEmail));
        }

        var reference = BuildCustomerReference(userEmail);
        var customer = await _maxioClient.GetCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        return await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<Customer> EnsureCustomerAsync(string userEmail, string reference, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.GetCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _maxioClient.CreateCustomerAsync(new CreateCustomer
            {
                FirstName = DeriveFirstName(userEmail),
                LastName = "User",
                Email = userEmail,
                Reference = reference
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserEmail} with reference {Reference}.",
                created.Id, userEmail, reference);

            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request may have created the customer first (reference is unique in Maxio).
            // Re-read by reference before giving up.
            var raced = await _maxioClient.GetCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<Subscription?> FindExistingSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !IsTerminal(s.State));
    }

    private static bool IsTerminal(string? state)
    {
        return state is not null && TerminalStates.Contains(state);
    }

    private static string BuildCustomerReference(string userEmail)
    {
        return $"{CustomerReferencePrefix}{userEmail}";
    }

    private static string DeriveFirstName(string userEmail)
    {
        var localPart = userEmail.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
    }
}
