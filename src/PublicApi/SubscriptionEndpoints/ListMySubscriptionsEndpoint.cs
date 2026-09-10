using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's own subscriptions as recorded in Maxio.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, HttpContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, ISubscriptionService subscriptionService) =>
                await HandleAsync(http, subscriptionService))
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, ISubscriptionService subscriptionService)
    {
        try
        {
            var response = new ListMySubscriptionsResponse();
            response.Subscriptions.AddRange(await subscriptionService.GetMySubscriptionsAsync(http.User, http.RequestAborted));
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return SubscriptionProblemMapper.ToProblem(ex);
        }
    }
}
