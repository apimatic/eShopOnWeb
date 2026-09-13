using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IMaxioCustomerService _customerService;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IMaxioCustomerService customerService,
        string productFamilyHandle)
    {
        _client = client;
        _customerService = customerService;
        _productFamilyHandle = productFamilyHandle;
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        var families = await _client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: ct);

        var family = families.FirstOrDefault(f =>
            string.Equals(f.ProductFamily?.Handle, _productFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.ProductFamily?.Id == null)
        {
            throw new InvalidOperationException($"Product family '{_productFamilyHandle}' not found.");
        }

        return (int)family.ProductFamily.Id.Value;
    }

    public async Task<IReadOnlyList<MaxioProductResult>> ListPlansAsync(CancellationToken ct)
    {
        var familyId = await ResolveProductFamilyIdAsync(ct);

        var products = await _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: familyId.ToString(),
            dateField: null,
            filter: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            includeArchived: null,
            include: null,
            ct: ct);

        return products
            .Where(p => p.Product != null)
            .Select(p => new MaxioProductResult
            {
                Id = (int)(p.Product!.Id ?? 0),
                Name = p.Product.Name,
                Handle = p.Product.Handle,
                Description = p.Product.Description,
                PriceInCents = p.Product.PriceInCents ?? 0,
                Interval = (int)(p.Product.Interval ?? 0),
                IntervalUnit = p.Product.IntervalUnit?.Value
            })
            .ToList();
    }

    public async Task<MaxioSubscriptionResult> SubscribeAsync(
        string email,
        string firstName,
        string lastName,
        string productHandle,
        string? reference,
        CancellationToken ct)
    {
        await _customerService.EnsureCustomerExistsAsync(email, firstName, lastName, ct);

        var subscriptionRequest = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = email,
                Reference = reference,
                PaymentCollectionMethod = CollectionMethod.Invoice
            }
        };

        var response = await _client.Subscriptions.CreateSubscription(body: subscriptionRequest, ct: ct);
        var s = response.Subscription!;
        return new MaxioSubscriptionResult
        {
            Id = (int)(s.Id ?? 0),
            State = s.State?.Value,
            ProductName = s.Product?.Name,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextAssessmentAt = s.NextAssessmentAt,
            ActivatedAt = s.ActivatedAt,
            CanceledAt = s.CanceledAt
        };
    }

    public async Task<IReadOnlyList<MaxioSubscriptionResult>> ListMySubscriptionsAsync(string email, CancellationToken ct)
    {
        CustomerResponse customerResponse;
        try
        {
            customerResponse = await _client.Customers.ReadCustomerByReference(reference: email, ct: ct);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<MaxioSubscriptionResult>();
        }

        var customerId = (int)customerResponse.Customer!.Id!.Value;
        var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);

        return subscriptions
            .Where(s => s.Subscription != null)
            .Select(s => new MaxioSubscriptionResult
            {
                Id = (int)(s.Subscription!.Id ?? 0),
                State = s.Subscription.State?.Value,
                ProductName = s.Subscription.Product?.Name,
                CurrentPeriodEndsAt = s.Subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = s.Subscription.NextAssessmentAt,
                ActivatedAt = s.Subscription.ActivatedAt,
                CanceledAt = s.Subscription.CanceledAt
            })
            .ToList();
    }
}
