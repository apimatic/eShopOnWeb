using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult, HttpContext, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (HttpContext context, SubscriptionService subscriptions) =>
                    await HandleAsync(context, subscriptions))
            .Produces<SubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext context, SubscriptionService subscriptions)
    {
        var plans = await subscriptions.ListPlansAsync(context.RequestAborted);
        return Results.Ok(new SubscriptionPlansResponse { Plans = plans });
    }
}
