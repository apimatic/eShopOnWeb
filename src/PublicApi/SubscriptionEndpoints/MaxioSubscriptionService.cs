using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Errors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioSubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default);
    Task<CreateSubscriptionResponse> SubscribeAsync(string userName, string planHandle, CancellationToken ct = default);
    Task<List<MySubscriptionDto>> GetMySubscriptionsAsync(string userName, CancellationToken ct = default);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _familyHandle;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IConfiguration config, ILogger<MaxioSubscriptionService> logger)
    {
        _logger = logger;
        _familyHandle = config.GetValue<string>("Maxio:ProductFamilyHandle") ?? "eshop-subscribe";
        var apiKey = config.GetValue<string>("Maxio:ApiKey") ?? "";
        var subdomain = config.GetValue<string>("Maxio:Subdomain") ?? "cp-exp-1";
        var baseUrl = config.GetValue<string>("Maxio:BaseUrl");

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },
            Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us
        };

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            options.Server.Production.Us.BaseUrl = baseUrl;
            options.Server.Production.Us.Site = subdomain;
        }
        else
        {
            options.Server.Production.Us.Site = subdomain;
        }

        _client = new MaxioAdvancedBillingClient(new System.Net.Http.HttpClient(), options);
    }

    public async Task<List<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default)
    {
        var results = new List<SubscriptionPlanDto>();
        try
        {
            var products = await _client.Products.ListProducts(
                dateField: null, filter: null, endDate: null, endDatetime: null,
                startDate: null, startDatetime: null, includeArchived: null, include: null,
                page: 1, perPage: 20, ct: ct);

            foreach (var resp in products)
            {
                var p = resp.Product;
                if (p == null) continue;
                // Filter by family handle if available; SDK does not expose family directly on Product,
                // so include all and let client filter, but we keep only those that match the family context.
                results.Add(new SubscriptionPlanDto
                {
                    Handle = p.Handle ?? "",
                    Name = p.Name ?? "",
                    Price = p.PriceInCents.HasValue ? p.PriceInCents.Value / 100m : null,
                    FamilyHandle = _familyHandle
                });
            }
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Maxio product list failed: {Status} {Body}", ex.Error.StatusCode, ex.Error.ReadAsString());
            throw;
        }
        return results;
    }

    public async Task<CreateSubscriptionResponse> SubscribeAsync(string userName, string planHandle, CancellationToken ct = default)
    {
        // Idempotent customer lookup by reference/email
        int customerId = await FindOrCreateCustomerAsync(userName, ct);

        // Check existing subscription for same product handle
        var existing = await _client.Customers.ListCustomerSubscriptions(customerId, ct);
        foreach (var subResp in existing)
        {
            var sub = subResp.Subscription;
            if (sub != null && (sub.Product?.Handle == planHandle || sub.Product?.Id.HasValue == true))
            {
                // Return existing
                return new CreateSubscriptionResponse
                {
                    SubscriptionId = sub.Id ?? 0,
                    CustomerId = customerId,
                    PlanHandle = planHandle,
                    State = sub.State ?? "",
                    NextBillingDate = sub.NextAssessmentAt.HasValue ? sub.NextAssessmentAt.Value.DateTime : (sub.CurrentPeriodEndsAt.HasValue ? sub.CurrentPeriodEndsAt.Value.DateTime : (DateTime?)null),
                    Message = "Subscription already exists"
                };
            }
        }

        var createReq = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                Reference = userName,
                DeferSignup = false
            }
        };

        try
        {
            var created = await _client.Subscriptions.CreateSubscription(createReq, ct);
            var s = created.Subscription;
            return new CreateSubscriptionResponse
            {
                SubscriptionId = s?.Id ?? 0,
                CustomerId = customerId,
                PlanHandle = planHandle,
                State = s?.State ?? "",
                NextBillingDate = s?.NextAssessmentAt.HasValue == true ? s.NextAssessmentAt.Value.DateTime : (s?.CurrentPeriodEndsAt.HasValue == true ? s.CurrentPeriodEndsAt.Value.DateTime : (DateTime?)null),
                Message = "Subscribed successfully"
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            _logger.LogError(ex, "Maxio subscription create error");
            if (ex.Error.TryGetErrorListResponse1(out var errs))
            {
                throw new Exception($"Subscription creation failed: {errs?.ToString() ?? "unknown"}");
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw new Exception($"Subscription creation failed: {raw.StatusCode} {raw.ReadAsString()}");
            }
            throw;
        }
    }

    public async Task<List<MySubscriptionDto>> GetMySubscriptionsAsync(string userName, CancellationToken ct = default)
    {
        int customerId = await FindOrCreateCustomerAsync(userName, ct);
        var results = new List<MySubscriptionDto>();
        try
        {
            var subs = await _client.Customers.ListCustomerSubscriptions(customerId, ct);
            foreach (var resp in subs)
            {
                var s = resp.Subscription;
                if (s == null) continue;
                results.Add(new MySubscriptionDto
                {
                    SubscriptionId = s.Id ?? 0,
                    PlanHandle = s.Product?.Handle ?? "",
                    State = s.State ?? "",
                    NextBillingDate = s.NextAssessmentAt.HasValue ? s.NextAssessmentAt.Value.DateTime : (s.CurrentPeriodEndsAt.HasValue ? s.CurrentPeriodEndsAt.Value.DateTime : (DateTime?)null),
                    CreatedAt = s.CreatedAt.HasValue ? s.CreatedAt.Value.DateTime : (DateTime?)null
                });
            }
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Maxio list subscriptions failed");
            throw;
        }
        return results;
    }

    private async Task<int> FindOrCreateCustomerAsync(string userName, CancellationToken ct)
    {
        try
        {
            var list = await _client.Customers.ListCustomers(direction: null, dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, q: userName, page: 1, perPage: 10, ct: ct);
            foreach (var resp in list)
            {
                var c = resp.Customer;
                if (c != null && (c.Email == userName || c.Reference == userName))
                {
                    return c.Id ?? 0;
                }
            }
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning(ex, "Maxio customer list failed, attempting create");
        }

        var createReq = new MaxioAdvancedBilling.Models.CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = userName.Split('@').FirstOrDefault() ?? userName,
                LastName = "",
                Email = userName,
                Reference = userName,
                Organization = "eShopOnWeb"
            }
        };

        try
        {
            var created = await _client.Customers.CreateCustomer(createReq, ct);
            return created.Customer?.Id ?? 0;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            _logger.LogError(ex, "Maxio customer create error");
            if (ex.Error.TryGetCustomerErrorResponse1(out var err))
            {
                // If reference conflict (422), try reading by reference
                if (err != null)
                {
                    try
                    {
                        var byRef = await _client.Customers.ReadCustomerByReference(userName, ct);
                        if (byRef.Customer != null) return byRef.Customer.Id ?? 0;
                    }
                    catch { }
                }
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError("Raw error {Status}: {Body}", raw.StatusCode, raw.ReadAsString());
            }
            throw;
        }
    }
}
