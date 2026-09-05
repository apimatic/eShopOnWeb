using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioSubscriptionService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioSubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                return await HandleAsync(subscriptionService, httpContext);
            })
           .Produces<ListMySubscriptionsResponse>()
           .WithName("GetMySubscriptions")
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService, HttpContext httpContext)
    {
        var response = new ListMySubscriptionsResponse(Guid.NewGuid());

        try
        {
            var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ??
                           httpContext.User.FindFirst("email")?.Value ??
                           httpContext.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                return Results.BadRequest("User email not found in token claims");
            }

            var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(userEmail);
            response.Subscriptions.AddRange(subscriptions);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
