using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioBillingService svc) => await HandleAsync(new MySubscriptionsRequest(), svc))
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionPlanEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioBillingService svc)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var username = user?.FindFirstValue(ClaimTypes.Name) ?? user?.Identity?.Name ?? "anonymous";
        var subs = await svc.GetCustomerSubscriptionsAsync(username);
        var response = new MySubscriptionsResponse(request.CorrelationId());
        foreach (var s in subs)
        {
            response.Subscriptions.Add(new MySubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                ProductName = s.ProductName,
                ProductHandle = s.ProductHandle,
                Price = s.PriceInCents / 100m,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
                ActivatedAt = s.ActivatedAt
            });
        }
        return Results.Ok(response);
    }
}
