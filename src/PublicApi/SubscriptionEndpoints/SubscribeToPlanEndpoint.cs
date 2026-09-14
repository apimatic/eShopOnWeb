using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the caller (identified from the bearer token, not a request field) to a plan.
/// Ensures a Maxio customer exists for the caller first (idempotent - a double-click never
/// creates two customers or two subscriptions to the same plan).
/// </summary>
public class SubscribeToPlanEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequestBody body, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                var buyerEmail = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value ?? string.Empty;
                var request = new SubscribeRequest(buyerEmail, body.PlanHandle);
                return await HandleAsync(request, billingService);
            })
            .Produces<SubscribeResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerEmail))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        try
        {
            var (subscription, wasCreated) = await billingService.SubscribeAsync(request.BuyerEmail, request.PlanHandle);

            var response = new SubscribeResponse(request.CorrelationId())
            {
                AlreadySubscribed = !wasCreated,
                Subscription = new SubscriptionDto
                {
                    MaxioSubscriptionId = subscription.MaxioSubscriptionId,
                    PlanHandle = subscription.PlanHandle,
                    PlanName = subscription.PlanName,
                    State = subscription.State,
                    PriceInCents = subscription.PriceInCents,
                    CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt = subscription.NextAssessmentAt,
                    CreatedAt = subscription.CreatedAt
                }
            };

            return wasCreated
                ? Results.Created($"api/my-subscriptions/{subscription.MaxioSubscriptionId}", response)
                : Results.Ok(response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return Results.NotFound(ex.Message);
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: (int)HttpStatusCode.BadGateway, title: "Maxio API error");
        }
    }
}
