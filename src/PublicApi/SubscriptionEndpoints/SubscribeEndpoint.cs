using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Ensures a Maxio customer exists for the shopper
/// (idempotent) and enrolls them; a repeated call for the same shopper and plan returns the existing
/// subscription rather than creating a second one.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken ct) =>
            {
                request.UserName = user.FindFirstValue(ClaimTypes.Name);
                return await HandleAsync(request, billing, ct);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billing)
        => HandleAsync(request, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
            return Results.Unauthorized();

        var response = new SubscribeResponse(request.CorrelationId());
        var subscriber = SubscriberIdentity.FromUserName(request.UserName);

        var subscription = await billing.SubscribeAsync(subscriber, request.PlanHandle, ct);
        response.Subscription = subscription.ToDto();

        return Results.Created("api/my-subscriptions", response);
    }
}
