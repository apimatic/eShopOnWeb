using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated caller's own subscriptions. Returns an empty list if they have never
/// subscribed - identity comes solely from the JWT, never from client-supplied input.
/// </summary>
public class GetMySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioSubscriptionService maxioSubscriptionService) =>
            {
                return await HandleAsync(new GetMySubscriptionsRequest(user.Identity!.Name!), maxioSubscriptionService);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, IMaxioSubscriptionService maxioSubscriptionService)
    {
        var response = new GetMySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await maxioSubscriptionService.GetSubscriptionsForUserAsync(request.Username);
        response.Subscriptions = subscriptions.Select(SubscriptionMapping.ToDto).ToList();

        return Results.Ok(response);
    }
}
