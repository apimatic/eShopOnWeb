using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Lists the current subscription plans in the configured Maxio product family.
/// </summary>
public class SubscriptionPlansEndpoint : IEndpoint<IResult>
{
    public Task<IResult> HandleAsync()
    {
        throw new NotSupportedException("This endpoint is invoked through its route handler.");
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptions, HttpContext context) =>
            {
                try
                {
                    var plans = await subscriptions.GetPlansAsync(context.RequestAborted);
                    return Results.Ok(new SubscriptionPlansResponse { Plans = plans.ToList() });
                }
                catch (MaxioApiException)
                {
                    return Results.Problem("Subscription plans are temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Produces<SubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");
    }
}
