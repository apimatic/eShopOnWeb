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
/// Subscribes the authenticated caller to a plan. Ensures a Maxio customer exists for the
/// eShopOnWeb user (idempotent) and enrolls them; a double-click will not create a second
/// customer or subscription. The caller's identity comes from the JWT, not the request body.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, System.Security.Claims.ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.CallerUserName = CallerIdentity.ResolveUserName(user);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var subscriber = SubscriberInfo.FromEmail(request.CallerUserName);
        var result = await subscriptionService.SubscribeAsync(subscriber, request.PlanHandle ?? string.Empty);

        response.Subscription = result.Subscription.ToDto();
        response.CustomerId = result.CustomerId;
        response.AlreadyExisted = result.AlreadyExisted;
        response.Message = result.AlreadyExisted
            ? $"You are already subscribed to '{result.Subscription.PlanName}' ({result.Subscription.State})."
            : $"Subscribed to '{result.Subscription.PlanName}' ({result.Subscription.State}). " +
              $"Next billing date: {result.Subscription.NextBillingDate:yyyy-MM-dd}.";

        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
