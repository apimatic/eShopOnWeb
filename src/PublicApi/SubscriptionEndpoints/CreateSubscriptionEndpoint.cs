using System.Linq;
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
/// Subscribes the authenticated shopper to a plan. Idempotent per shopper: repeating the request returns the
/// existing subscription instead of creating another.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, httpContext, billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status400BadRequest)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status409Conflict)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status422UnprocessableEntity)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            response.Status = "invalid_plan";
            response.Message = "planHandle is required.";
            return Results.BadRequest(response);
        }

        var result = await billingService.SubscribeAsync(userName, request.PlanHandle, request.FirstName, request.LastName,
            httpContext.RequestAborted);

        response.Message = result.Message;
        response.Errors = result.Errors?.ToList();
        response.Subscription = result.Subscription is null ? null : SubscriptionDto.From(result.Subscription);

        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                response.Status = "created";
                return Results.Created("api/my-subscriptions", response);
            case SubscribeOutcome.AlreadySubscribed:
                response.Status = "existing";
                return Results.Ok(response);
            case SubscribeOutcome.Pending:
                response.Status = "pending";
                return Results.Accepted("api/my-subscriptions", response);
            case SubscribeOutcome.SubscribedToOtherPlan:
                response.Status = "conflict";
                return Results.Conflict(response);
            case SubscribeOutcome.UnknownPlan:
                response.Status = "invalid_plan";
                return Results.BadRequest(response);
            case SubscribeOutcome.Rejected:
                response.Status = "rejected";
                return Results.UnprocessableEntity(response);
            default:
                return Results.Unauthorized();
        }
    }
}
