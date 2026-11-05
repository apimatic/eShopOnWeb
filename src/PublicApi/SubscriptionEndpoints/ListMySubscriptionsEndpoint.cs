using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the authenticated user's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, SubscriptionService service, CancellationToken ct) =>
            {
                return await HandleAsync(user, service, ct);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, SubscriptionService service, CancellationToken ct)
    {
        var userName = SubscriptionEndpointHelpers.UserName(user);
        if (userName is null)
            return Results.Unauthorized();

        var response = new ListMySubscriptionsResponse();
        try
        {
            response.Subscriptions.AddRange(await service.ListMySubscriptionsAsync(userName, ct));
            return Results.Ok(response);
        }
        catch (SubscriptionException ex)
        {
            return SubscriptionEndpointHelpers.Failure(ex);
        }
    }
}
