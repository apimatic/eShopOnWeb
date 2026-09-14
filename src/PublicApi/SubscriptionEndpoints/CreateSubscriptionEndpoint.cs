using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling user to a plan. Idempotent: ensures a Maxio customer exists for the
/// user and enrolls them, returning the existing live subscription instead of creating a
/// duplicate if the user already subscribed to this plan (e.g. a double-click retry).
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService) =>
            {
                request.UserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? user.FindFirst(ClaimTypes.Name)?.Value
                    ?? string.Empty;
                request.UserEmail = user.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "planHandle is required." });
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await subscriptionService.SubscribeAsync(request.UserId, request.UserEmail, request.PlanHandle);
            response.Subscription = new SubscriptionDto
            {
                SubscriptionId = subscription.SubscriptionId,
                State = subscription.State,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                PriceInCents = subscription.PriceInCents,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                CreatedAt = subscription.CreatedAt
            };
            return Results.Ok(response);
        }
        catch (MaxioPlanNotFoundException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
        catch (MaxioApiException ex) when (ex.IsClientError)
        {
            return Results.BadRequest(new { message = "Maxio rejected the subscription request.", detail = ex.Message });
        }
    }
}
