using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan. Ensures a Maxio customer exists for the
/// user (idempotent) and enrolls them. A double-submit cannot create two live
/// subscriptions to the same plan.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal, ISubscriptionFacade>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionFacade subscriptionFacade) =>
            {
                return await HandleAsync(request, user, subscriptionFacade);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, ISubscriptionFacade subscriptionFacade)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return Results.BadRequest(new Dictionary<string, string> { ["planHandle"] = "A plan handle is required." });
        }

        try
        {
            var outcome = await subscriptionFacade.SubscribeAsync(user, request.PlanHandle);
            response.Subscription = SubscriptionEndpointMapping.ToDto(outcome.Subscription);
            response.AlreadySubscribed = outcome.AlreadySubscribed;
            response.Message = outcome.AlreadySubscribed
                ? $"You are already subscribed to '{outcome.Subscription.PlanName}'."
                : $"Subscribed to '{outcome.Subscription.PlanName}'.";

            return outcome.AlreadySubscribed
                ? Results.Ok(response)
                : Results.Created($"/api/my-subscriptions", response);
        }
        catch (PlanNotFoundException ex)
        {
            return Results.NotFound(new { ex.PlanHandle, message = ex.Message });
        }
        catch (MaxioApiException ex)
        {
            // Upstream billing error. Surface it without leaking internal details.
            return Results.Problem(
                detail: string.Join("; ", ex.Errors),
                statusCode: StatusCodes.Status502BadGateway,
                title: "The billing provider rejected the request.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status401Unauthorized, title: "Caller identity could not be resolved.");
        }
    }
}
