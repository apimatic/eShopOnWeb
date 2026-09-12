using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Claims;
using MinimalApi.Endpoint;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionListEndpoint : IEndpoint<IResult, MySubscriptionListRequest, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _accessor;
    public MySubscriptionListEndpoint(IHttpContextAccessor accessor) { _accessor = accessor; }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioBillingService svc) => await HandleAsync(new MySubscriptionListRequest(), svc))
            .Produces<MySubscriptionListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionListRequest request, IMaxioBillingService svc)
    {
        var user = _accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();

        var reference = user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue("sub") ?? "unknown";
        try
        {
            var subs = await svc.ListMySubscriptionsAsync(reference);
            var resp = new MySubscriptionListResponse();
            resp.Subscriptions = subs.Select(s => new MySubscriptionDto(s.Id, s.State, s.PlanHandle, s.Price, s.NextBillingDate)).ToList();
            return Results.Ok(resp);
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: 500);
        }
    }
}

public class MySubscriptionListRequest : BaseRequest { }

public class MySubscriptionListResponse : BaseResponse
{
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}

public record MySubscriptionDto(int Id, string State, string PlanHandle, decimal Price, string NextBillingDate);


