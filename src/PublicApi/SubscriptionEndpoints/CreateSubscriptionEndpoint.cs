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

public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (
                CreateSubscriptionRequest request,
                IMaxioSubscriptionService service,
                HttpContext httpContext) =>
            {
                var email = httpContext.User.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(email))
                {
                    return Results.Unauthorized();
                }

                var firstName = httpContext.User.FindFirstValue("firstName");
                var lastName = httpContext.User.FindFirstValue("lastName");

                var result = await service.SubscribeAsync(email, firstName, lastName, request.ProductHandle);

                var response = new CreateSubscriptionResponse
                {
                    Subscription = result
                };

                return Results.Ok(response);
            })
            .Produces<CreateSubscriptionResponse>()
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .WithTags("SubscriptionEndpoints");
    }
}
