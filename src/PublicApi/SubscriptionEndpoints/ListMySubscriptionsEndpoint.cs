using System;
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
/// Lists the authenticated caller's subscriptions (GET /api/my-subscriptions).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(user, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billingService)
    {
        var subscriber = SubscriberIdentityFactory.FromPrincipal(user);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await billingService.ListSubscriptionsAsync(subscriber);
            var response = new ListMySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(s => s.ToDto()).ToList(),
            };
            return Results.Ok(response);
        }
        catch (Exception ex) when (SubscriptionErrors.IsHandled(ex))
        {
            return SubscriptionErrors.ToResult(ex);
        }
    }
}
