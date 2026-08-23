using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionListEndpoint : IEndpoint<IResult, HttpContext, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context, ISubscriptionBillingService billingService) =>
                await HandleAsync(context, billingService))
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext context, ISubscriptionBillingService billingService)
    {
        var subscriptions = await billingService.ListMySubscriptionsAsync(
            context.User.Identity?.Name ?? string.Empty,
            context.RequestAborted);
        return Results.Ok(new MySubscriptionsResponse(subscriptions));
    }
}
