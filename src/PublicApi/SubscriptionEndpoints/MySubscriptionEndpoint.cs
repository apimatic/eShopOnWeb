using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionEndpoint : IEndpoint<IResult, MySubscriptionRequest, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (MaxioBillingService service, HttpContext ctx) =>
            {
                var email = ctx.User.FindFirstValue(ClaimTypes.Name) ?? "";
                if (string.IsNullOrWhiteSpace(email)) return Results.Unauthorized();
                return await HandleAsync(new MySubscriptionRequest { Email = email }, service);
            })
            .Produces<List<SubscribeResponse>>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionRequest request, MaxioBillingService service)
    {
        var email = request.Email;
        try
        {
            var customers = await service.Client.Customers.ListCustomers(q: email, direction: null, dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, page: 1, perPage: 50, ct: default);
            var customer = customers.FirstOrDefault(c => c.Customer?.Reference == email || c.Customer?.Email == email)?.Customer;
            if (customer == null) return Results.Ok(new List<SubscribeResponse>());

            var subs = await service.Client.Subscriptions.ListSubscriptions(state: null, product: null, productPricePointId: null, coupon: null, couponCode: null, dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, metadata: null, direction: null, sort: null, include: null, page: 1, perPage: 50, ct: default);
            var list = subs
                .Where(s => s.Subscription?.Customer?.Id == customer.Id)
                .Select(s => new SubscribeResponse
                {
                    SubscriptionId = s.Subscription?.Id ?? 0,
                    State = s.Subscription?.State.ToString() ?? "",
                    ProductHandle = s.Subscription?.Product?.Handle ?? "",
                    CustomerReference = email,
                    NextBillingDate = s.Subscription?.NextAssessmentAt?.ToString("yyyy-MM-dd") ?? ""
                })
                .ToList();
            return Results.Ok(list);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: 500);
        }
    }
}
