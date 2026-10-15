using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

// Sole SDK boundary: bounded calls, sanitized errors, and minimal DTOs at the HTTP boundary.
public sealed class MaxioGateway(MaxioAdvancedBilling.MaxioAdvancedBillingClient client,
    MaxioSettings settings, ILogger<MaxioGateway> logger)
{
    public async Task<PlanList> PlansAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        for (var page = 1; page <= 100; page++)
        {
            var items = await CallAsync(t => client.Products.ListProducts(
                new MaxioAdvancedBilling.Requests.Products.ListProductsRequest
                { Page = page, PerPage = 200, IncludeArchived = false }, cancellationToken: t), ct);
            if (items == null) throw Unreadable();
            foreach (var item in items)
            {
                var p = item?.Product;
                if (p?.ProductFamily?.Handle != settings.ProductFamilyHandle || p.ArchivedAt != null
                    || p.ProductFamily.ArchivedAt != null) continue;
                if (string.IsNullOrWhiteSpace(p.Handle) || string.IsNullOrWhiteSpace(p.Name)
                    || p.PriceInCents == null || p.Interval == null || p.IntervalUnit == null) throw Unreadable();
                plans.Add(new(p.Handle, p.Name, p.Description, p.PriceInCents.Value,
                    p.Interval.Value, p.IntervalUnit.Value, p.RequireCreditCard == true));
            }
            if (items.Count < 200) return new(plans, false);
        }
        return new(plans, true);
    }

    public async Task<MaxioAdvancedBilling.Models.Customer?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await CallAsync(t => client.Customers.ReadCustomerByReference(
                new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: t), ct);
            return Customer(response, reference);
        }
        catch (BillingException ex) when (ex.StatusCode == 404) { return null; }
    }

    public async Task<MaxioAdvancedBilling.Models.Customer> CreateCustomerAsync(string reference,
        string email, SubscribeRequest request, CancellationToken ct)
    {
        var response = await CallAsync(t => client.Customers.CreateCustomer(
            new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
            {
                Body = new MaxioAdvancedBilling.Models.CreateCustomerRequest
                {
                    Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                    { FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(), Email = email, Reference = reference }
                }
            }, cancellationToken: t), ct);
        return Customer(response, reference);
    }

    private static MaxioAdvancedBilling.Models.Customer Customer(MaxioAdvancedBilling.Models.CustomerResponse response, string reference)
    {
        if (response?.Customer?.Id is not > 0 || response.Customer.Reference != reference) throw Unreadable();
        return response.Customer;
    }

    public async Task<MaxioAdvancedBilling.Models.Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await CallAsync(t => client.Subscriptions.FindSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest { Reference = reference }, cancellationToken: t), ct);
            if (response?.Subscription?.Id is not > 0 || response.Subscription.Reference != reference) throw Unreadable();
            return response.Subscription;
        }
        catch (BillingException ex) when (ex.StatusCode == 404) { return null; }
    }

    public async Task<MaxioAdvancedBilling.Models.Subscription> CreateSubscriptionAsync(string reference, int customerId,
        string handle, CancellationToken ct)
    {
        var response = await CallAsync(t => client.Subscriptions.CreateSubscription(
            new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
            {
                Body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                {
                    Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                    {
                        CustomerId = customerId, ProductHandle = handle, Reference = reference,
                        PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                    }
                }
            }, cancellationToken: t), ct);
        if (response?.Subscription?.Id is not > 0 || response.Subscription.Reference != reference) throw Unreadable();
        return response.Subscription;
    }

    public async Task<IReadOnlyList<MaxioAdvancedBilling.Models.Subscription>> SubscriptionsAsync(int customerId, CancellationToken ct)
    {
        var response = await CallAsync(t => client.Customers.ListCustomerSubscriptions(
            new MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: t), ct);
        if (response == null || response.Any(x => x?.Subscription == null)) throw Unreadable();
        return response.Select(x => x.Subscription!).ToArray();
    }

    public static SubscriptionView View(MaxioAdvancedBilling.Models.Subscription s, int customerId)
    {
        if (s.Id is not > 0 || s.Customer?.Id != customerId || string.IsNullOrWhiteSpace(s.Product?.Handle)
            || string.IsNullOrWhiteSpace(s.Product.Name) || s.ProductPriceInCents == null || s.State == null
            || string.IsNullOrWhiteSpace(s.Currency)) throw Unreadable();
        return new(s.Id.Value, s.Product.Handle, s.Product.Name, s.ProductPriceInCents.Value,
            s.Currency, s.State.Value, s.NextAssessmentAt);
    }

    private static BillingException Unreadable() => new(502, "Billing returned an incomplete response. The outcome may still be pending.");

    private async Task<T> CallAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try { return await call(deadline.Token); }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _)) logger.LogWarning("Billing customer validation rejected; status {Status}", (int)ex.StatusCode);
            else if (ex.Error.TryGetRawError(out _)) logger.LogWarning("Billing customer rejected; status {Status}", (int)ex.StatusCode);
            throw Rejected(ex.StatusCode);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out _)) logger.LogWarning("Billing subscription validation rejected; status {Status}", (int)ex.StatusCode);
            else if (ex.Error.TryGetRawError(out _)) logger.LogWarning("Billing subscription rejected; status {Status}", (int)ex.StatusCode);
            throw Rejected(ex.StatusCode);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ResponseDeserializationException ex)
        {
            logger.LogWarning("Billing response could not be read; status {Status}", (int)ex.StatusCode);
            // A malformed lookup must never be mistaken for an absent record.
            if ((int)ex.StatusCode >= 200 && (int)ex.StatusCode < 300) throw Unreadable();
            throw new BillingException(ex.StatusCode == HttpStatusCode.NotFound ? 502 : PublicStatus(ex.StatusCode), "Billing returned an unreadable error response.");
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException ex)
        {
            logger.LogWarning("Billing request rejected; status {Status}", (int)ex.StatusCode);
            throw Rejected(ex.StatusCode);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.SdkException ex)
        {
            logger.LogWarning("Billing unavailable; kind {Kind}", ex.GetType().Name);
            throw new BillingException(503, "Billing is temporarily unavailable.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new BillingException(503, "Billing exceeded its response deadline."); }
    }

    private static int PublicStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.NotFound => 404,
        HttpStatusCode.UnprocessableEntity => 422,
        HttpStatusCode.TooManyRequests => 503,
        HttpStatusCode.Conflict => 409,
        _ => 502
    };
    private static BillingException Rejected(HttpStatusCode code) => new(PublicStatus(code),
        code == HttpStatusCode.UnprocessableEntity ? "Billing could not accept these subscription details. Check the plan and customer details." : "Billing could not complete the request.");
}


