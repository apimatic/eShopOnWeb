using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services.Maxio;

public interface IMaxioSubscriptionService
{
    Task<int> EnsureCustomerAsync(string reference, string? firstName = null, string? lastName = null, string? email = null, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlanOptionsAsync(CancellationToken ct = default);
    Task<SubscriptionDto> SubscribeAsync(string customerReference, string productHandle, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsForCustomerAsync(string customerReference, CancellationToken ct = default);
}

public sealed class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
}

public sealed class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public string Reference { get; set; } = string.Empty;
}

public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, MaxioSettings settings, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<int> EnsureCustomerAsync(string reference, string? firstName = null, string? lastName = null, string? email = null, CancellationToken ct = default)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            if (existing?.Customer != null)
            {
                _logger.LogInformation("Maxio customer found by reference {Ref} (id {Id})", reference, existing.Customer.Id);
                return existing.Customer.Id ?? 0;
            }
        }
        catch (Exception ex) when (ex is SdkException<RawError> || ex.Message.Contains("404") || ex.Message.Contains("NotFound"))
        {
            // Expected when not found; proceed to create.
        }

        try
        {
            var body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName ?? reference,
                    LastName = lastName ?? reference,
                    Email = email ?? $"{reference}@example.com",
                    Reference = reference
                }
            };
            var created = await _client.Customers.CreateCustomer(body, ct: ct);
            if (created?.Customer != null)
            {
                _logger.LogInformation("Maxio customer created for reference {Ref} (id {Id})", reference, created.Customer.Id);
                return created.Customer.Id ?? 0;
            }
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            _logger.LogWarning(ex, "CreateCustomer conflict for {Ref}; retrying read", reference);
            try
            {
                var retry = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
                if (retry?.Customer != null) return retry.Customer.Id ?? 0;
            }
            catch { }
            throw;
        }

        throw new InvalidOperationException("Unable to ensure Maxio customer for reference " + reference);
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlanOptionsAsync(CancellationToken ct = default)
    {
        var familyHandle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle)) familyHandle = "eshop-subscribe";

        var products = await _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: familyHandle,
            dateField: null,
            filter: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            includeArchived: null,
            include: null,
            page: 1,
            perPage: 20,
            ct: ct);

        var result = new List<SubscriptionPlanDto>();
        foreach (var resp in products)
        {
            var p = resp?.Product;
            if (p == null) continue;
            result.Add(new SubscriptionPlanDto
            {
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? p.Handle ?? string.Empty,
                Price = 0,
                Currency = "USD"
            });
        }
        return result;
    }

    public async Task<SubscriptionDto> SubscribeAsync(string customerReference, string productHandle, CancellationToken ct = default)
    {
        int customerId = await EnsureCustomerAsync(customerReference, ct: ct);

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerReference = customerReference,
                CustomerId = customerId > 0 ? customerId : null,
                ProductHandle = productHandle
            }
        };

        var resp = await _client.Subscriptions.CreateSubscription(body, ct: ct);
        var sub = resp?.Subscription;
        if (sub == null) throw new InvalidOperationException("Subscription creation returned no data.");

        return new SubscriptionDto
        {
            Id = sub.Id ?? 0,
            State = sub.State ?? string.Empty,
            ProductHandle = sub.NextProductHandle ?? productHandle,
            Price = 0,
            NextBillingDate = sub.NextAssessmentAt ?? sub.CurrentPeriodEndsAt,
            Reference = sub.Reference ?? string.Empty
        };
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsForCustomerAsync(string customerReference, CancellationToken ct = default)
    {
        int customerId = 0;
        try
        {
            var c = await _client.Customers.ReadCustomerByReference(customerReference, ct: ct);
            customerId = c?.Customer?.Id ?? 0;
        }
        catch
        {
            return new List<SubscriptionDto>();
        }

        if (customerId <= 0) return new List<SubscriptionDto>();

        var subs = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
        return subs.Select(s => new SubscriptionDto
        {
            Id = s?.Subscription?.Id ?? 0,
            State = s?.Subscription?.State ?? string.Empty,
            ProductHandle = s?.Subscription?.NextProductHandle ?? string.Empty,
            Price = 0,
            NextBillingDate = s?.Subscription?.NextAssessmentAt ?? s?.Subscription?.CurrentPeriodEndsAt,
            Reference = s?.Subscription?.Reference ?? string.Empty
        }).ToList();
    }
}
