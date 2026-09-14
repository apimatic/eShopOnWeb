using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated shopper in a subscription plan. Ensures a Maxio customer exists
/// (idempotent) and creates the subscription; a repeated call for an already-live plan returns
/// the existing subscription rather than a duplicate. POST /api/subscriptions
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, SubscriberIdentity, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                var subscriber = SubscriberIdentityResolver.Resolve(user);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, subscriber, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, SubscriberIdentity subscriber, ISubscriptionBillingService billingService)
        => HandleAsync(request, subscriber, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        SubscriberIdentity subscriber,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { Message = "planHandle is required. Call GET /api/subscription-plans for available plan handles." });
        }

        try
        {
            var result = await billingService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);

            response.Subscription = result.Subscription.ToDto();
            response.AlreadyEnrolled = result.AlreadyEnrolled;

            // Already enrolled -> 200 OK (idempotent no-op). New enrollment -> 201 Created.
            return result.AlreadyEnrolled
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }
        catch (PlanNotFoundException ex)
        {
            return Results.NotFound(new { ex.Message, ex.PlanHandle });
        }
    }
}
