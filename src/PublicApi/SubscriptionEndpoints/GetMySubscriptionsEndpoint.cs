using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the calling shopper's own Maxio subscriptions.
/// </summary>
public class GetMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioSubscriptionService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService maxioSubscriptionService, HttpContext httpContext) =>
            {
                return await HandleAsync(maxioSubscriptionService, httpContext);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService maxioSubscriptionService, HttpContext httpContext)
    {
        var username = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var identity = MaxioCustomerIdentity.FromEShopUsername(username);
        var subscriptions = await maxioSubscriptionService.GetSubscriptionsForCustomerAsync(identity, httpContext.RequestAborted);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionSummaryDto.FromServiceDto));

        return Results.Ok(response);
    }
}
