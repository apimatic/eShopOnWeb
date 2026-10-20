using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, HttpContext, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, SubscriptionService service) =>
            {
                return await HandleAsync(http, service);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, SubscriptionService service)
    {
        var response = new ListMySubscriptionsResponse(new SubscribeRequest().CorrelationId());

        var caller = await BillingCaller.ResolveAsync(http);
        if (caller is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await service.ListMySubscriptionsAsync(caller, http.RequestAborted);
        response.Subscriptions = subscriptions.Select(SubscriptionDto.From).ToList();

        return Results.Ok(response);
    }
}
