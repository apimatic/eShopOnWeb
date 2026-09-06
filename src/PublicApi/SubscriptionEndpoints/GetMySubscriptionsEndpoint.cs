using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager, HttpContext httpContext) =>
            {
                var response = new GetMySubscriptionsResponse();

                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var user = await userManager.FindByIdAsync(userId);
                if (user == null)
                {
                    return Results.NotFound("User not found");
                }

                try
                {
                    var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(user);
                    response.Subscriptions.AddRange(subscriptions);
                    return Results.Ok(response);
                }
                catch (Exception)
                {
                    return Results.StatusCode(500);
                }
            })
           .Produces<GetMySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService subscriptionService)
    {
        return await Task.FromResult(Results.BadRequest());
    }
}
