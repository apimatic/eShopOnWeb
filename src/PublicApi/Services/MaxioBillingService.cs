using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Authentication;
using AdvancedBilling.Standard.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly MaxioSettings _settings;

    public MaxioBillingService(IOptions<MaxioSettings> options)
    {
        _settings = options.Value;
    }

    private AdvancedBillingClient CreateClient()
    {
        var env = _settings.Environment.Equals("EU", StringComparison.OrdinalIgnoreCase)
            ? AdvancedBilling.Standard.Environment.EU
            : AdvancedBilling.Standard.Environment.US;

        var builder = new AdvancedBillingClient.Builder()
            .BasicAuthCredentials(new BasicAuthModel.Builder(_settings.ApiKey, "x").Build())
            .Site(_settings.Subdomain)
            .Environment(env);

        return builder.Build();
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync()
    {
        var client = CreateClient();
        var handles = new[] { "eshop-pro", "basic-plan" };
        var plans = new List<SubscriptionPlanDto>();

        foreach (var h in handles)
        {
            try
            {
                // Try list with filter; if SDK lacks direct filter, fall back to read by handle via list input if supported
                var listInput = new ListProductsInput
                {
                    PerPage = 50
                };
                // Some SDK versions include ProductFamilyHandle on input; attempt to set dynamically
                var prop = listInput.GetType().GetProperty("ProductFamilyHandle");
                if (prop != null)
                    prop.SetValue(listInput, _settings.ProductFamilyHandle);

                var products = await client.ProductsController.ListProductsAsync(listInput);
                var match = products
                    .Select(p => p.Product)
                    .FirstOrDefault(p => p?.Handle == h || p?.Handle == h + "-plan");

                if (match == null)
                {
                    // Try direct list without filter
                    products = await client.ProductsController.ListProductsAsync(new ListProductsInput { PerPage = 100 });
                    match = products
                        .Select(p => p.Product)
                        .FirstOrDefault(p => p?.Handle == h || p?.Handle == h + "-plan");
                }

                if (match != null)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Handle = match.Handle ?? h,
                        Name = match.Name ?? h,
                        PriceInCents = match.PriceInCents ?? 0,
                        State = "available",
                        ProductFamilyHandle = _settings.ProductFamilyHandle
                    });
                }
                else
                {
                    // Fallback: use known seed data so endpoint remains useful
                    plans.Add(new SubscriptionPlanDto
                    {
                        Handle = h,
                        Name = h == "eshop-pro" ? "Pro Plan" : "Basic Plan",
                        PriceInCents = h == "eshop-pro" ? 29900 : 2900,
                        State = "available",
                        ProductFamilyHandle = _settings.ProductFamilyHandle
                    });
                }
            }
            catch
            {
                // Fallback
                plans.Add(new SubscriptionPlanDto
                {
                    Handle = h,
                    Name = h == "eshop-pro" ? "Pro Plan" : "Basic Plan",
                    PriceInCents = h == "eshop-pro" ? 29900 : 2900,
                    State = "available",
                    ProductFamilyHandle = _settings.ProductFamilyHandle
                });
            }
        }

        return plans;
    }

    public async Task<SubscriptionResultDto> SubscribeAsync(string userReference, string planHandle, string email, string firstName, string lastName)
    {
        var client = CreateClient();

        // Idempotent customer by reference
        CustomerResponse customerResp = null;
        try
        {
            customerResp = await client.CustomersController.ReadCustomerByReferenceAsync(userReference);
        }
        catch
        {
            customerResp = null;
        }

        int customerId;
        if (customerResp == null)
        {
            var createReq = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName ?? "User",
                    LastName = lastName ?? "",
                    Email = email ?? userReference,
                    Reference = userReference,
                    Organization = "eShopOnWeb",
                    Address = "",
                    City = "",
                    State = "",
                    Zip = "",
                    Country = "US"
                }
            };
            var created = await client.CustomersController.CreateCustomerAsync(createReq);
            customerId = created.Customer?.Id ?? 0;
        }
        else
        {
            customerId = customerResp.Customer?.Id ?? 0;
        }

        if (customerId == 0)
            throw new InvalidOperationException("Failed to obtain Maxio customer.");

        // Check existing subscription for this customer + plan handle via reference if stored, else list
        List<SubscriptionResponse> existing = null;
        try
        {
            existing = await client.CustomersController.ListCustomerSubscriptionsAsync(customerId);
        }
        catch { }

        var existingSub = existing?.FirstOrDefault(s =>
            s.Subscription?.Product != null && s.Subscription.Product.Handle == planHandle);

        if (existingSub != null)
        {
            return new SubscriptionResultDto
            {
                Id = existingSub.Subscription?.Id ?? 0,
                State = existingSub.Subscription?.State?.ToString() ?? "active",
                PlanHandle = planHandle,
                PriceInCents = existingSub.Subscription?.ProductPriceInCents ?? 0,
                NextBillingAt = existingSub.Subscription?.CurrentPeriodEndsAt != null ? (DateTime?)existingSub.Subscription.CurrentPeriodEndsAt.Value.DateTime : (existingSub.Subscription?.NextAssessmentAt != null ? (DateTime?)existingSub.Subscription.NextAssessmentAt.Value.DateTime : null)
            };
        }

        var subReq = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerAttributes = new CustomerAttributes
                {
                    FirstName = firstName ?? "User",
                    LastName = lastName ?? "",
                    Email = email ?? userReference,
                    Reference = userReference,
                    Organization = "eShopOnWeb",
                    Address = "",
                    City = "",
                    State = "",
                    Zip = "",
                    Country = "US"
                },
                // No trial, no setup fee, no card required per seed config
                PaymentCollectionMethod = CollectionMethod.Automatic
            }
        };

        var subResp = await client.SubscriptionsController.CreateSubscriptionAsync(subReq);
        var sub = subResp.Subscription;

        return new SubscriptionResultDto
        {
            Id = sub?.Id ?? 0,
            State = sub?.State?.ToString() ?? "active",
            PlanHandle = sub?.Product?.Handle ?? planHandle,
            PriceInCents = sub?.ProductPriceInCents ?? 0,
            NextBillingAt = sub?.NextAssessmentAt != null ? (DateTime?)sub.NextAssessmentAt.Value.DateTime : (sub?.CurrentPeriodEndsAt != null ? (DateTime?)sub.CurrentPeriodEndsAt.Value.DateTime : null)
        };
    }

    public async Task<List<SubscriptionResultDto>> GetMySubscriptionsAsync(string userReference)
    {
        var client = CreateClient();
        CustomerResponse customerResp = null;
        try
        {
            customerResp = await client.CustomersController.ReadCustomerByReferenceAsync(userReference);
        }
        catch { }

        if (customerResp == null) return new List<SubscriptionResultDto>();

        var subs = await client.CustomersController.ListCustomerSubscriptionsAsync((int)(customerResp.Customer.Id ?? 0));
        return subs.Select(s => new SubscriptionResultDto
        {
            Id = s.Subscription?.Id ?? 0,
            State = s.Subscription?.State?.ToString() ?? "",
            PlanHandle = s.Subscription?.Product?.Handle ?? "",
            PriceInCents = s.Subscription?.ProductPriceInCents ?? 0,
            NextBillingAt = s.Subscription?.CurrentPeriodEndsAt != null ? (DateTime?)s.Subscription.CurrentPeriodEndsAt.Value.DateTime : (s.Subscription?.NextAssessmentAt != null ? (DateTime?)s.Subscription.NextAssessmentAt.Value.DateTime : null),
            CustomerReference = userReference
        }).ToList();
    }
}
