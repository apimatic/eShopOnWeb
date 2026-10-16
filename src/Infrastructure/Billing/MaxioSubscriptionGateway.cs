using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioSubscriptionGateway(MaxioAdvancedBilling.MaxioAdvancedBillingClient client,
    IOptions<MaxioSettings> settings, ILogger<MaxioSubscriptionGateway> logger) : ISubscriptionGateway
{
    public string Scope => settings.Value.Scope;
    public string ProductFamilyHandle => settings.Value.ProductFamilyHandle;

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken ct) => ExecuteAsync<IReadOnlyList<SubscriptionPlan>>("ListPlans", false, async () =>
    {
        var plans = new List<SubscriptionPlan>();
        for (int page = 1; page <= 25; page++)
        {
            var response = await client.ProductFamilies.ListProductsForProductFamily(new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest
            {
                ProductFamilyId = "handle:" + ProductFamilyHandle, Page = page, PerPage = 200, IncludeArchived = false
            }, cancellationToken: ct);
            if (response is null) throw InvalidResponse();
            foreach (var item in response)
            {
                var p = item?.Product;
                if (p is null || string.IsNullOrWhiteSpace(p.Handle)) throw InvalidResponse();
                if (p.ProductFamily?.Handle is not null && p.ProductFamily.Handle != ProductFamilyHandle) throw InvalidResponse();
                if (p.ArchivedAt is not null || p.ProductFamily?.ArchivedAt is not null) continue;
                if (p.Interval is not > 0 || p.IntervalUnit is null) continue;
                plans.Add(new SubscriptionPlan(p.Handle, p.Name ?? p.Handle, p.Description, p.PriceInCents,
                    p.Interval, p.IntervalUnit.Value, p.RequireCreditCard, p.RequireBillingAddress, p.RequireShippingAddress));
            }
            if (response.Count < 200) return plans.DistinctBy(x => x.Handle).ToList();
        }
        throw new BillingException("catalog_incomplete", "The complete plan catalog could not be loaded. Please try again later.", 503);
    });

    public Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken ct) => ExecuteAsync<BillingCustomer?>("FindCustomer", false, async () =>
    {
        try
        {
            var response = await client.Customers.ReadCustomerByReference(new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            return Customer(response?.Customer, reference);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    });

    public Task<BillingCustomer> CreateCustomerAsync(BillingAccount account, string reference, CancellationToken ct) => ExecuteAsync("CreateCustomer", true, async () =>
    {
        var response = await client.Customers.CreateCustomer(new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
        {
            Body = new MaxioAdvancedBilling.Models.CreateCustomerRequest
            {
                Customer = new MaxioAdvancedBilling.Models.CreateCustomer { FirstName = account.FirstName, LastName = account.LastName, Email = account.Email, Reference = reference }
            }
        }, cancellationToken: ct);
        return Customer(response?.Customer, reference);
    });

    public Task<IReadOnlyList<OwnedSubscription>> GetSubscriptionsAsync(int customerId, CancellationToken ct) => ExecuteAsync<IReadOnlyList<OwnedSubscription>>("ListSubscriptions", false, async () =>
    {
        var response = await client.Customers.ListCustomerSubscriptions(new MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct);
        if (response is null) throw InvalidResponse();
        return response.Select(x => Project(x?.Subscription)).ToList();
    });

    public Task<OwnedSubscription?> FindSubscriptionAsync(string reference, CancellationToken ct) => ExecuteAsync<OwnedSubscription?>("FindSubscription", false, async () =>
    {
        try
        {
            var response = await client.Subscriptions.FindSubscription(new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
            return Project(response?.Subscription);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            if (ex.Error.TryGetNoContent(out _)) return null;
            if (ex.Error.TryGetRawError(out _)) return null;
            throw;
        }
    });

    public Task<OwnedSubscription> CreateSubscriptionAsync(int customerId, string handle, string reference, CancellationToken ct) => ExecuteAsync("CreateSubscription", true, async () =>
    {
        var response = await client.Subscriptions.CreateSubscription(new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
        {
            Body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    CustomerId = customerId, ProductHandle = handle, Reference = reference,
                    PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                }
            }
        }, cancellationToken: ct);
        return Project(response?.Subscription);
    });

    private static BillingCustomer Customer(MaxioAdvancedBilling.Models.Customer? customer, string reference)
    {
        if (customer?.Id is not > 0 || customer.Reference != reference) throw InvalidResponse();
        return new BillingCustomer(customer.Id.Value, reference);
    }

    private static OwnedSubscription Project(MaxioAdvancedBilling.Models.Subscription? sub)
    {
        if (sub?.Id is not > 0 || sub.Customer?.Id is not > 0 || string.IsNullOrEmpty(sub.Customer.Reference)) throw InvalidResponse();
        return new OwnedSubscription(new SubscriptionDetails(sub.Id.Value, sub.Product?.Handle, sub.Product?.Name,
            sub.ProductPriceInCents, sub.CurrentBillingAmountInCents, sub.Currency, sub.State?.Value,
            sub.NextAssessmentAt, sub.Product?.Interval, sub.Product?.IntervalUnit?.Value), sub.Customer.Id.Value, sub.Customer.Reference, sub.Reference);
    }

    private static BillingException InvalidResponse() => new("provider_response_invalid", "Billing returned an incomplete or inconsistent response.", outcomeUnknown: true);

    private async Task<T> ExecuteAsync<T>(string operation, bool write, Func<Task<T>> call)
    {
        var timer = Stopwatch.StartNew();
        try { return await call(); }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                // Inspect every declared variant, but never expose personal data in provider messages.
                if (body.Errors?.TryGetCustomerError(out _) == true) { }
                else if (body.Errors?.TryGetListOfString(out _) == true) { }
            }
            else if (ex.Error.TryGetRawError(out _)) { }
            throw ProviderError(operation, (int)ex.StatusCode, write);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out _)) { }
            else if (ex.Error.TryGetRawError(out _)) { }
            throw ProviderError(operation, (int)ex.StatusCode, write);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _)) { }
            else if (ex.Error.TryGetRawError(out _)) { }
            throw ProviderError(operation, (int)ex.StatusCode, write);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException ex) { throw ProviderError(operation, (int)ex.StatusCode, write); }
        catch (MaxioAdvancedBilling.Core.Exceptions.SdkException) { throw ProviderError(operation, null, write); }
        catch (OperationCanceledException) when (write) { throw ProviderError(operation, null, true); }
        finally { logger.LogInformation("Billing {Operation} finished in {ElapsedMs} ms; trace {TraceId}", operation, timer.ElapsedMilliseconds, Activity.Current?.TraceId.ToString()); }
    }

    private BillingException ProviderError(string operation, int? status, bool write)
    {
        logger.LogWarning("Billing {Operation} returned {ProviderStatus}; trace {TraceId}", operation, status, Activity.Current?.TraceId.ToString());
        bool unknown = write && (status is null or >= 500 or >= 200 and < 300 or 408 or 429);
        int mapped = status switch { 401 or 403 => 502, 429 => 503, >= 400 and < 500 => status.Value, _ => 502 };
        string message = mapped == 422 ? "Maxio rejected the customer or enrollment details. Check names and the plan's payment and address requirements." : "Billing is temporarily unavailable. Please try again later.";
        return new BillingException(unknown ? "billing_outcome_unknown" : mapped == 422 ? "billing_validation" : "billing_unavailable", message, mapped, unknown);
    }
}
