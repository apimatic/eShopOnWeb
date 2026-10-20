using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// Lists the signed-in user's subscriptions. Identity comes from the token only.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptions, HttpContext httpContext) =>
            {
                return await HandleAsync(new MySubscriptionsRequest(), subscriptions, httpContext);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptions, HttpContext httpContext)
    {
        var userName = SubscriptionEndpointSupport.UserName(httpContext.User);
        if (string.IsNullOrWhiteSpace(userName)) return Results.Unauthorized();

        var mine = await subscriptions.ListMineAsync(userName, request.CorrelationId(), httpContext.RequestAborted);
        return Results.Ok(new MySubscriptionsResponse(request.CorrelationId()) { Subscriptions = mine.ToList() });
    }
}
