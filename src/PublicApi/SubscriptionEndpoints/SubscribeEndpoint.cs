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
/// Enrolls the calling user in a plan (UC1). Idempotent on the caller's identity — a repeat call
/// while already subscribed to the same product returns the existing subscription.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal>
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscribeEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/subscribe",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user)
    {
        Guard.Against.Null(user.Identity?.Name, nameof(user.Identity.Name));
        var userReference = user.Identity!.Name!;

        // eShopOnWeb's ApplicationUser carries no first/last name; derive a placeholder from the
        // email's local part (Maxio requires both non-empty on customer creation).
        var localPart = userReference.Split('@')[0];

        var response = new SubscribeResponse(request.CorrelationId());
        var subscription = await _subscriptionService.SubscribeAsync(userReference, userReference, localPart, "eShopOnWeb", request.ProductHandle);
        response.Subscription = subscription.ToDto();
        return Results.Ok(response);
    }
}
