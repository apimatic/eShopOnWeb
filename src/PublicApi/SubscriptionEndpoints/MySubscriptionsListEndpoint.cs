using System;
using System.Linq;
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
/// Lists the calling user's subscriptions (identity from the JWT). Creates nothing.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, MaxioSubscriptionService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioSubscriptionService subscriptions, HttpContext httpContext) =>
            {
                return await HandleAsync(subscriptions, httpContext);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService subscriptions, HttpContext httpContext)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var correlationId = Guid.NewGuid();
        try
        {
            var items = await subscriptions.ListMySubscriptionsAsync(userName, correlationId, httpContext.RequestAborted);
            return Results.Ok(new ListMySubscriptionsResponse(correlationId)
            {
                Subscriptions = items.Select(SubscriptionEndpointMapping.ToDto).ToList(),
            });
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointMapping.Failure(ex);
        }
    }
}
