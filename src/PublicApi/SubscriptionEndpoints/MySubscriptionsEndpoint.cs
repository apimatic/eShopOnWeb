using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext http, MaxioSubscriptionService svc) =>
            {
                var user = http.User.Identity?.Name ?? http.User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(user))
                    return Results.Unauthorized();
                return await HandleAsync(new MySubscriptionsRequest { UserReference = user }, svc);
            })
            .Produces<List<MySubscriptionResponse>>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, MaxioSubscriptionService service)
    {
        try
        {
            var subs = await service.ListSubscriptionsForCustomerAsync(request.UserReference, request.CancellationToken);
            var result = subs.Select(s => new MySubscriptionResponse
            {
                SubscriptionId = s.Subscription?.Id ?? 0,
                PlanHandle = s.Subscription?.Product?.Handle ?? s.Subscription?.Product?.Name ?? "",
                State = s.Subscription?.State.ToString() ?? "",
                NextBillingDate = s.Subscription?.NextAssessmentAt?.ToString("yyyy-MM-dd") ?? null
            }).ToList();
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return Results.Problem(title: "List failed", detail: ex.Message, statusCode: 500);
        }
    }
}
