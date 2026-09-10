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
/// POST /api/subscriptions — subscribes the authenticated shopper to a plan. The subscriber comes from the
/// token; the body carries only the plan handle. Idempotent: a repeated call returns the existing subscription
/// rather than creating a duplicate.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, HttpContext, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, HttpContext httpContext, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(request, httpContext, billingService);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, HttpContext httpContext,
        IMaxioBillingService billingService)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(detail: "planHandle is required.",
                statusCode: StatusCodes.Status400BadRequest, title: "Subscription request failed");
        }

        if (!SubscriptionEndpointHelpers.TryGetSubscriber(httpContext.User, out var subscriber))
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await billingService.SubscribeAsync(subscriber, request.PlanHandle.Trim(),
                httpContext.RequestAborted);

            response.Subscription = SubscriptionDto.From(result.Subscription);
            response.AlreadySubscribed = result.AlreadyExisted;
            response.Message = result.AlreadyExisted
                ? $"You are already subscribed to {response.Subscription.PlanName}."
                : $"Subscribed to {response.Subscription.PlanName}. Next billing on "
                    + $"{response.Subscription.NextBillingDate:yyyy-MM-dd}.";

            // A fresh enrollment is a creation (201); an idempotent hit returns the existing resource (200).
            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"/api/my-subscriptions", response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointHelpers.ToErrorResult(ex);
        }
    }
}
