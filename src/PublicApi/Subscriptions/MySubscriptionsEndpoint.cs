using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Returns subscriptions owned by the authenticated eShop user.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult>
{
    public Task<IResult> HandleAsync()
    {
        throw new NotSupportedException("This endpoint is invoked through its route handler.");
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptions, UserManager<ApplicationUser> userManager, HttpContext context) =>
            {
                var user = await CreateSubscriptionEndpoint.GetCurrentUserAsync(context, userManager);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var items = await subscriptions.GetMySubscriptionsAsync(user, context.RequestAborted);
                    return Results.Ok(new MySubscriptionsResponse { Subscriptions = items.ToList() });
                }
                catch (MaxioApiException)
                {
                    return Results.Problem("Subscriptions are temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("Subscriptions");
    }
}
