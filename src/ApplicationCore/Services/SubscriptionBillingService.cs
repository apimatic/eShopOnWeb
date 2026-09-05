using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    // Subscription states that mean the enrollment is over and a new subscribe attempt should
    // not be treated as a duplicate. Every other state (active, trialing, past_due, on_hold,
    // etc.) is considered "still current" for idempotency purposes.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
    };

    private readonly IMaxioBillingClient _client;
    private readonly BuyerEnrollmentGate _enrollmentGate;
    private readonly MaxioSettings _settings;

    public SubscriptionBillingService(IMaxioBillingClient client, BuyerEnrollmentGate enrollmentGate, IOptions<MaxioSettings> settings)
    {
        _client = client;
        _enrollmentGate = enrollmentGate;
        _settings = settings.Value;
    }

    public Task<IReadOnlyList<MaxioProduct>> GetAvailablePlansAsync()
        => _client.ListProductFamilyProductsAsync(_settings.ProductFamilyHandle);

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(string buyerReference, string email, string planHandle)
    {
        using (await _enrollmentGate.AcquireAsync(buyerReference))
        {
            var customer = await _client.FindCustomerByReferenceAsync(buyerReference)
                ?? await CreateCustomerAsync(buyerReference, email);

            var existing = await FindCurrentSubscriptionAsync(customer.Id, planHandle);
            if (existing is not null)
            {
                return new SubscriptionEnrollmentResult(existing, AlreadyExisted: true);
            }

            var created = await _client.CreateSubscriptionAsync(new MaxioSubscriptionCreate
            {
                ProductHandle = planHandle,
                CustomerReference = buyerReference,
            });

            return new SubscriptionEnrollmentResult(created, AlreadyExisted: false);
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForBuyerAsync(string buyerReference)
    {
        var customer = await _client.FindCustomerByReferenceAsync(buyerReference);
        if (customer is null) return Array.Empty<MaxioSubscription>();

        return await _client.ListCustomerSubscriptionsAsync(customer.Id);
    }

    private async Task<MaxioCustomer> CreateCustomerAsync(string buyerReference, string email)
    {
        var (firstName, lastName) = DeriveNameFromEmail(email);
        return await _client.CreateCustomerAsync(new MaxioCustomerCreate
        {
            Reference = buyerReference,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
        });
    }

    private async Task<MaxioSubscription?> FindCurrentSubscriptionAsync(long customerId, string planHandle)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !TerminalStates.Contains(s.State));
    }

    private static (string FirstName, string LastName) DeriveNameFromEmail(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        static string Capitalize(string value) => char.ToUpperInvariant(value[0]) + value[1..];

        if (parts.Length >= 2) return (Capitalize(parts[0]), Capitalize(parts[1]));
        if (parts.Length == 1 && parts[0].Length > 0) return (Capitalize(parts[0]), "Customer");
        return ("eShopOnWeb", "Customer");
    }
}
