using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Linq;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (HttpContext http, CreateSubscriptionRequest req, MaxioSubscriptionService svc) =>
            {
                var user = http.User.Identity?.Name ?? http.User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(user))
                    return Results.Unauthorized();
                req.UserReference = user;
                req.Email = user; // use identity as email/reference
                return await HandleAsync(req, svc);
            })
            .Produces<CreateSubscriptionResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioSubscriptionService service)
    {
        try
        {
            var customerRef = request.UserReference ?? request.PlanHandle;
            await service.EnsureCustomerAsync(customerRef, request.Email ?? customerRef, ct: request.CancellationToken);
            var subId = await service.FindOrCreateSubscriptionAsync(customerRef, request.PlanHandle, request.CancellationToken);

            // Build minimal response; fetch subscription for details
            var subs = await service.ListSubscriptionsForCustomerAsync(customerRef, request.CancellationToken);
            var sub = subs.FirstOrDefault(s => s.Subscription?.Id == subId);

            var resp = new CreateSubscriptionResponse
            {
                Subscribed = true,
                SubscriptionId = subId,
                PlanHandle = request.PlanHandle,
                State = sub?.Subscription?.State.ToString() ?? "active",
                NextBillingDate = sub?.Subscription?.NextAssessmentAt?.ToString("yyyy-MM-dd") ?? null
            };
            return Results.Ok(resp);
        }
        catch (Exception ex)
        {
            return Results.Problem(title: "Subscription failed", detail: ex.Message, statusCode: 500);
        }
    }
}
