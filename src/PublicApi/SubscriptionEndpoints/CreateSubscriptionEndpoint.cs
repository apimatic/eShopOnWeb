using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: ensures a single Maxio customer for
/// the shopper and returns an existing live subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                // Identity comes from the token, never the body.
                var reference = user.Identity?.Name;
                request.SubscriberReference = reference;
                request.SubscriberEmail = reference;
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.SubscriberReference))
            return Results.Unauthorized();

        var identity = new SubscriberIdentity(
            request.SubscriberReference!,
            string.IsNullOrWhiteSpace(request.SubscriberEmail) ? request.SubscriberReference! : request.SubscriberEmail!);

        var subscription = await billingService.SubscribeAsync(identity, request.PlanHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionMappings.ToDto(subscription),
        };

        // Fresh enrolment → 201; idempotent no-op (already subscribed) → 200.
        var statusCode = subscription.AlreadyExisted ? StatusCodes.Status200OK : StatusCodes.Status201Created;
        return Results.Json(response, statusCode: statusCode);
    }
}
