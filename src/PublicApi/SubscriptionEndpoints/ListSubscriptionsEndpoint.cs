using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionsEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                var response = new ListSubscriptionsResponse(Guid.NewGuid());

                try
                {
                    var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrEmpty(userId))
                    {
                        return Results.Unauthorized();
                    }

                    response.Subscriptions = await subscriptionService.GetUserSubscriptionsAsync(userId);
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error fetching subscriptions: {ex.Message}");
                }
            })
            .Produces<ListSubscriptionsResponse>()
            .WithName("ListUserSubscriptions")
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() => throw new NotImplementedException();
}
