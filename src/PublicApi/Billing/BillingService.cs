using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.Products;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Shared Maxio-backed operations used by the billing endpoints.
/// </summary>
public class BillingService
{
    private const int PageSize = 200;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly UserManager<ApplicationUser> _userManager;

    public BillingService(MaxioAdvancedBillingClient client, UserManager<ApplicationUser> userManager)
    {
        _client = client;
        _userManager = userManager;
    }

    public MaxioAdvancedBillingClient Client => _client;

    /// <summary>The caller's identity, taken from the JWT.</summary>
    public static string GetUserName(ClaimsPrincipal user)
    {
        var name = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new BillingException(401, "The token does not identify a user.");
        return name;
    }

    /// <summary>All plans a shopper can subscribe to: every non-archived Maxio product.</summary>
    public async Task<List<Product>> ListPlansAsync()
    {
        var plans = new List<Product>();
        for (var page = 1; ; page++)
        {
            var batch = await _client.Products.ListProducts(new ListProductsRequest { Page = page, PerPage = PageSize, IncludeArchived = false });
            plans.AddRange(batch.Select(p => p.Product).Where(p => p.ArchivedAt == null));
            if (batch.Count < PageSize) break;
        }
        return plans;
    }

    public async Task<Product> GetPlanByHandleAsync(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
            throw BillingException.BadRequest("A plan handle is required.");
        try
        {
            var plan = (await _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle.Trim() })).Product;
            if (plan.ArchivedAt != null)
                throw BillingException.Unprocessable($"Plan '{handle}' is no longer available.");
            return plan;
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 404)
        {
            throw BillingException.NotFound($"Plan '{handle}' does not exist.");
        }
    }

    public async Task<Product> GetPlanByIdAsync(int planId)
    {
        try
        {
            return (await _client.Products.ReadProduct(new ReadProductRequest { ProductId = planId })).Product;
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 404)
        {
            throw BillingException.NotFound($"Plan {planId} does not exist.");
        }
    }

    /// <summary>Finds the Maxio customer that stands for the signed-in shopper (reference = user name), creating it on first use.</summary>
    public async Task<Customer> EnsureCustomerAsync(string userName)
    {
        var existing = await FindCustomerAsync(userName);
        if (existing != null) return existing;

        var email = (await _userManager.FindByNameAsync(userName))?.Email;
        if (string.IsNullOrWhiteSpace(email))
            email = userName.Contains('@') ? userName : userName + "@example.com";
        var local = email!.Split('@')[0];

        try
        {
            var created = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = local,
                        LastName = "Shopper",
                        Email = email!,
                        Reference = userName,
                    },
                },
            });
            return created.Customer;
        }
        catch (ApiException)
        {
            // A concurrent request may have created the customer in the meantime.
            var raced = await FindCustomerAsync(userName);
            if (raced != null) return raced;
            throw;
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference)
    {
        try
        {
            var customer = (await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference })).Customer;
            return string.Equals(customer.Reference, reference, StringComparison.Ordinal) ? customer : null;
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 404)
        {
            return null;
        }
    }

    /// <summary>Reads a subscription and makes sure it belongs to the caller; someone else's subscription looks like a missing one.</summary>
    public async Task<Subscription> GetOwnedSubscriptionAsync(int subscriptionId, ClaimsPrincipal user)
    {
        var userName = GetUserName(user);
        var subscription = await GetSubscriptionAsync(subscriptionId);
        if (!string.Equals(subscription.Customer?.Reference, userName, StringComparison.Ordinal))
            throw BillingException.NotFound($"Subscription {subscriptionId} does not exist.");
        return subscription;
    }

    public async Task<Subscription> GetSubscriptionAsync(int subscriptionId)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId });
            return response.Subscription ?? throw BillingException.NotFound($"Subscription {subscriptionId} does not exist.");
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 404)
        {
            throw BillingException.NotFound($"Subscription {subscriptionId} does not exist.");
        }
    }

    public static PlanDto ToDto(Product plan) => new()
    {
        Id = plan.Id ?? 0,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents ?? 0,
        Price = (plan.PriceInCents ?? 0) / 100m,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit?.Value,
        Brand = plan.ProductFamily?.Name,
    };

    public static SubscriptionDto ToDto(Subscription s)
    {
        var balance = s.BalanceInCents ?? 0;
        return new SubscriptionDto
        {
            Id = s.Id ?? 0,
            State = s.State?.Value,
            Paid = balance <= 0,
            BalanceInCents = balance,
            PlanId = s.Product?.Id,
            PlanHandle = s.Product?.Handle,
            PlanName = s.Product?.Name,
            PriceInCents = s.ProductPriceInCents,
            Currency = s.Currency,
            NextBillingAt = s.NextAssessmentAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            CanceledAt = s.CanceledAt,
            OnHoldAt = s.OnHoldAt,
            CreatedAt = s.CreatedAt,
        };
    }
}
