using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUserSubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            Handler)
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetUserSubscriptions")
            .Produces<ListSubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized);
    }

    public Task<IResult> HandleAsync(EmptyRequest request)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> Handler(HttpContext httpContext, IMaxioSubscriptionService service)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var response = new ListSubscriptionsResponse();

        try
        {
            var subscriptions = await service.GetUserSubscriptionsAsync(userId);
            response.Subscriptions.AddRange(subscriptions);
            return Results.Ok(response);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
