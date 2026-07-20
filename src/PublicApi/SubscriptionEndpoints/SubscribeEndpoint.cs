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
/// Enrolls the authenticated caller in a plan (UC1). Idempotent — returns the existing live
/// subscription instead of creating a duplicate.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                Guard.Against.NullOrEmpty(user.Identity?.Name, nameof(user.Identity.Name));
                request.UserName = user.Identity!.Name!;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var firstName = SubscriptionEndpointsShared.FirstNameFrom(request.UserName);
        var subscription = await subscriptionService.SubscribeAsync(request.UserName, request.UserName, firstName, "eShopOnWeb Customer", request.ProductHandle);

        response.Subscription = SubscriptionDto.FromModel(subscription);
        return Results.Ok(response);
    }
}
