using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: a repeated call for
/// the same user and plan returns the existing subscription instead of creating a
/// duplicate.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request,
             ClaimsPrincipal principal,
             UserManager<ApplicationUser> userManager,
             ISubscriptionBillingService billingService,
             CancellationToken cancellationToken) =>
            {
                var identity = await SubscriberIdentity.ResolveAsync(principal, userManager);
                if (identity is null)
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(request?.PlanHandle))
                {
                    return Results.BadRequest("planHandle is required.");
                }

                request.SetSubscriber(identity);
                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<SubscribeResponse>()
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    // Interface member (MinimalApi.Endpoint) — delegates to the cancellation-aware overload.
    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billingService)
        => HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var command = new SubscribeCommand(
            userReference: request.UserReference!,
            email: request.Email!,
            firstName: request.FirstName!,
            lastName: request.LastName!,
            planHandle: request.PlanHandle!);

        var result = await billingService.SubscribeAsync(command, cancellationToken);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = CustomerSubscriptionDto.FromDomain(result.Subscription),
            AlreadySubscribed = result.AlreadySubscribed
        };

        // A freshly-created subscription returns 201; an idempotent no-op returns 200.
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions", response);
    }
}
