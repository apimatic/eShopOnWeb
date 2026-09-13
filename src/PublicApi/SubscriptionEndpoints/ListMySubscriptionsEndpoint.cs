using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (
                IMaxioSubscriptionService service,
                HttpContext httpContext) =>
            {
                var email = httpContext.User.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(email))
                {
                    return Results.Unauthorized();
                }

                var subscriptions = await service.GetMySubscriptionsAsync(email);

                var response = new ListMySubscriptionsResponse
                {
                    Subscriptions = new List<SubscriptionDto>(subscriptions)
                };

                return Results.Ok(response);
            })
            .Produces<ListMySubscriptionsResponse>()
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .WithTags("SubscriptionEndpoints");
    }
}
