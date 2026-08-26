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
/// Subscribes the authenticated shopper to a plan
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal claimsPrincipal, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, claimsPrincipal, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal claimsPrincipal, ISubscriptionService subscriptionService)
    {
        var userName = claimsPrincipal.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest("ProductHandle is required.");
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscription = await subscriptionService.SubscribeAsync(userName, request.ProductHandle);

        response.Subscription = subscription;
        return Results.Ok(response);
    }
}
