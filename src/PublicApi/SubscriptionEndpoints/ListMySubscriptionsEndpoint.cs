using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions as recorded in Maxio.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(user.Identity?.Name, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(401)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(string? userName, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        try
        {
            var subscriptions = await subscriptionService.ListUserSubscriptionsAsync(userName, userName);
            response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMappers.MapSubscription));
        }
        catch (BillingException ex)
        {
            return Results.Problem(
                title: "The billing system request failed.",
                detail: ex.Message,
                statusCode: ex.StatusCode is >= 400 and < 500 ? 400 : StatusCodes.Status502BadGateway);
        }

        return Results.Ok(response);
    }
}
