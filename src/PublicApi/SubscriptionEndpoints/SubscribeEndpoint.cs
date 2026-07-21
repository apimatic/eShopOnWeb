using System.Security.Claims;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// UC1 — enrolls the authenticated user in a plan (or returns their existing active
/// subscription if already enrolled). Mirrors <c>CreateCatalogItemEndpoint</c>'s auth shape.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/subscribe",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.UserName = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        Guard.Against.NullOrEmpty(request.UserName, nameof(request.UserName));

        var response = new SubscribeResponse(request.CorrelationId());

        var subscription = await subscriptionService.SubscribeAsync(request.UserName, firstName: request.UserName, lastName: request.UserName, request.PlanHandle);
        response.Subscription = SubscriptionDto.FromDomain(subscription);

        return Results.Ok(response);
    }
}
