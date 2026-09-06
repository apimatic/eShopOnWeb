using System.Collections.Generic;
using System.Linq;
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
/// Subscribes the authenticated shopper to a plan: ensures a billing customer exists for them, then enrols
/// them. The operation is idempotent — repeating it returns the existing live subscription with
/// <c>created: false</c> and HTTP 200 instead of enrolling the shopper a second time.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, SubscriberIdentity, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest? request,
             ClaimsPrincipal principal,
             ISubscriberIdentityResolver identityResolver,
             ISubscriptionBillingService billingService) =>
            {
                // An absent or literal-null body is treated as an empty one, so it fails validation
                // rather than the request pipeline.
                request ??= new SubscribeRequest();

                var subscriber = await identityResolver.ResolveAsync(principal, request.FirstName, request.LastName);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, subscriber, billingService);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        SubscribeRequest request,
        SubscriberIdentity subscriber,
        ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            var available = await ListPlanHandlesAsync(billingService);
            return Results.BadRequest(new
            {
                statusCode = StatusCodes.Status400BadRequest,
                message = "'planHandle' is required.",
                availablePlanHandles = available
            });
        }

        // Deliberately not tied to the caller's request-abort token: enrolment talks to an external system
        // of record and must not be abandoned half-way just because the client hung up.
        var result = await billingService.SubscribeAsync(subscriber, request.PlanHandle!, CancellationToken.None);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Created = result.Created,
            Subscription = SubscriptionDto.FromSubscription(result.Subscription)
        };

        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }

    private static async Task<IReadOnlyList<string>> ListPlanHandlesAsync(ISubscriptionBillingService billingService)
    {
        var plans = await billingService.ListPlansAsync(CancellationToken.None);
        return plans.Select(plan => plan.Handle).ToList();
    }
}
