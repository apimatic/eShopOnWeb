using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling shopper to a plan. Ensures a Maxio customer exists for the user
/// (idempotent) and enrolls them; a repeated request for a plan they already hold returns the
/// existing subscription rather than creating a duplicate.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request,
                   ClaimsPrincipal user,
                   UserManager<ApplicationUser> userManager,
                   IMaxioSubscriptionService subscriptionService) =>
            {
                var subscriber = await SubscriberContext.ResolveAsync(user, userManager);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                request.Subscriber = subscriber;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioSubscriptionService subscriptionService)
    {
        if (request.Subscriber is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(
                title: "A plan handle is required.",
                detail: "Provide the 'planHandle' of the plan to subscribe to.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var result = await subscriptionService.SubscribeAsync(request.Subscriber, request.PlanHandle);

            var response = new SubscribeResponse(request.CorrelationId())
            {
                Subscription = result.Subscription.ToDto(),
                AlreadySubscribed = result.AlreadyExisted,
                Message = result.AlreadyExisted
                    ? $"You are already subscribed to {result.Subscription.PlanName}."
                    : $"You are now subscribed to {result.Subscription.PlanName}.",
            };

            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return Results.Problem(
                title: "Subscription plan not found.",
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (MaxioApiException ex)
        {
            // A 4xx from Maxio means the request was well-formed but could not be fulfilled
            // (e.g. the plan requires a payment method); surface it as 422. Otherwise it's an
            // upstream/billing-provider failure (502).
            var statusCode = ex.StatusCode is >= 400 and < 500
                ? StatusCodes.Status422UnprocessableEntity
                : StatusCodes.Status502BadGateway;

            return Results.Problem(
                title: "Unable to complete the subscription with the billing provider.",
                detail: ex.Message,
                statusCode: statusCode);
        }
    }
}
