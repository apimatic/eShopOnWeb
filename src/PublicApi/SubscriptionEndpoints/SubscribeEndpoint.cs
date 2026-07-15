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
/// UC1 steps 2-7 — enroll the authenticated caller in a plan. Idempotent: returns the existing
/// subscription rather than double-enrolling.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.UserId = user.Identity!.Name!;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new SubscriptionResponse(request.CorrelationId());

        // eShopOnWeb Identity's username is always the user's email (Register.cshtml.cs), so it doubles
        // as both the stable billing-provider customer reference and the customer's email address.
        var subscription = await subscriptionService.SubscribeAsync(request.UserId, request.UserId, request.ProductHandle);
        response.Subscription = SubscriptionMapping.ToDto(subscription);

        return Results.Ok(response);
    }
}
