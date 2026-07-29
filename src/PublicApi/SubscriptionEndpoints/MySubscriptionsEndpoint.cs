using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// GET /api/my-subscriptions — returns the authenticated shopper's subscriptions. Returns an empty list
/// (without creating anything) when the shopper has never subscribed. JWT-authenticated; the caller
/// identity comes from the token.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService, BillingUser>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService,
             UserManager<ApplicationUser> userManager,
             HttpContext httpContext) =>
            {
                var user = await BillingUserResolver.ResolveAsync(httpContext.User, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(subscriptionService, user.Value);
            })
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService, BillingUser user)
    {
        try
        {
            var subscriptions = await subscriptionService.GetSubscriptionsAsync(user);
            var response = new MySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(SubscriptionDto.FromMaxio).ToList()
            };
            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            return MaxioProblem.ToResult(ex);
        }
    }
}
