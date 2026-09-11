using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscribeRequest req, MaxioBillingService service, HttpContext ctx) =>
            {
                req.Email = ctx.User.FindFirstValue(ClaimTypes.Name) ?? "";
                if (string.IsNullOrWhiteSpace(req.Email))
                    return Results.Unauthorized();
                return await HandleAsync(req, service);
            })
            .Produces<SubscribeResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, MaxioBillingService service)
    {
        try
        {
            var email = request.Email;
            // Idempotent customer
            var customers = await service.Client.Customers.ListCustomers(q: email, direction: null, dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, page: 1, perPage: 50, ct: default);
            var customer = customers.FirstOrDefault(c => c.Customer?.Reference == email || c.Customer?.Email == email)?.Customer;
            if (customer == null)
            {
                var created = await service.Client.Customers.CreateCustomer(new MaxioAdvancedBilling.Models.CreateCustomerRequest
                {
                    Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                    {
                        Email = email,
                        Reference = email,
                        FirstName = "",
                        LastName = ""
                    }
                }, ct: default);
                customer = created?.Customer;
            }
            if (customer == null)
                return Results.Problem("Failed to resolve customer", statusCode: 500);

            // Check existing subscription for this customer + product handle
            var subs = await service.Client.Subscriptions.ListSubscriptions(state: null, product: null, productPricePointId: null, coupon: null, couponCode: null, dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, metadata: null, direction: null, sort: null, include: null, page: 1, perPage: 50, ct: default);
            var existing = subs.FirstOrDefault(s => s.Subscription?.Customer?.Id == customer.Id && s.Subscription?.Product?.Handle == request.PlanHandle)?.Subscription;
            if (existing != null)
            {
                return Results.Ok(new SubscribeResponse
                {
                    SubscriptionId = existing.Id ?? 0,
                    State = existing.State.ToString() ?? "active",
                    ProductHandle = existing.Product?.Handle ?? request.PlanHandle,
                    CustomerReference = email,
                    NextBillingDate = existing.NextAssessmentAt?.ToString("yyyy-MM-dd") ?? ""
                });
            }

            var subReq = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    ProductHandle = request.PlanHandle,
                    CustomerReference = email,
                    // State set via active if needed; leaving default active per sandbox seeding
                }
            };

            // If enum doesn't match, fall back to string initialization; SDK uses StringEnum or direct set depending on model
            var createdSub = await service.Client.Subscriptions.CreateSubscription(subReq, ct: default);
            var sub = createdSub?.Subscription;
            if (sub == null)
                return Results.Problem("Failed to create subscription", statusCode: 500);

            return Results.Ok(new SubscribeResponse
            {
                SubscriptionId = sub.Id ?? 0,
                State = sub.State.ToString() ?? "active",
                ProductHandle = sub.Product?.Handle ?? request.PlanHandle,
                CustomerReference = email,
                NextBillingDate = sub.NextAssessmentAt?.ToString("yyyy-MM-dd") ?? ""
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: 500);
        }
    }
}
