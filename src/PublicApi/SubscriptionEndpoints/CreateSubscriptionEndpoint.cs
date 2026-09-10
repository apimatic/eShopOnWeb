using System;
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
/// Subscribes the authenticated caller to a plan (POST /api/subscriptions). Ensures a
/// billing customer exists for the caller (idempotent) and enrolls them; a repeated
/// submit for a plan the caller is already subscribed to returns the existing
/// subscription rather than creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, user, billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(
                title: "Missing plan",
                detail: "A 'planHandle' is required to subscribe.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var subscriber = SubscriberIdentityFactory.FromPrincipal(user);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await billingService.SubscribeAsync(subscriber, request.PlanHandle.Trim());
            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = result.Subscription.ToDto(),
                AlreadyExisted = result.AlreadyExisted,
            };

            // A replayed subscribe (double-click) returns 200 with the existing resource;
            // a brand-new enrollment returns 201 Created.
            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"api/my-subscriptions/{response.Subscription.Id}", response);
        }
        catch (Exception ex) when (SubscriptionErrors.IsHandled(ex))
        {
            return SubscriptionErrors.ToResult(ex);
        }
    }
}
