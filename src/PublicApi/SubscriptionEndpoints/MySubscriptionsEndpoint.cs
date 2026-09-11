using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (ISubscriptionService svc, HttpContext ctx) =>
        {
            return await HandleWithContext(new MySubscriptionsRequest(), svc, ctx);
        })
        .Produces<MySubscriptionsResponse>()
        .WithTags("SubscriptionEndpoints")
        .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService svc)
    {
        return Results.BadRequest(new MySubscriptionsResponse(request.CorrelationId()) { Message = "Context missing." });
    }

    private async Task<IResult> HandleWithContext(MySubscriptionsRequest request, ISubscriptionService svc, HttpContext ctx)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());
        var userName = ctx.User?.FindFirst(ClaimTypes.Name)?.Value ?? ctx.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            response.Message = "User identity missing.";
            return Results.BadRequest(response);
        }
        response.Subscriptions = await svc.GetMySubscriptionsAsync(userName);
        return Results.Ok(response);
    }
}

public class MySubscriptionsRequest : BaseRequest { }

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public IReadOnlyList<MySubscriptionDto> Subscriptions { get; set; } = new List<MySubscriptionDto>();
    public string Message { get; set; } = "";
}
