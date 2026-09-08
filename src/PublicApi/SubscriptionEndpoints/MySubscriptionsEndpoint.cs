using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(user, subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal principal, ISubscriptionService subscriptionService)
    {
        try
        {
            var subscriptions = await subscriptionService.GetMySubscriptionsAsync(principal, CancellationToken.None);
            var response = new MySubscriptionsResponse();
            response.Subscriptions.AddRange(subscriptions);
            return Results.Ok(response);
        }
        catch (SubscriptionAccessDeniedException)
        {
            return Results.Unauthorized();
        }
        catch (MaxioApiException ex)
        {
            return Results.Json(new ErrorDetails { StatusCode = StatusCodes.Status502BadGateway, Message = ex.Message },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
