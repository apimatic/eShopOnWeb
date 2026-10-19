using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling shopper to a plan. Idempotent per shopper: repeating the call (double-click,
/// client retry) never creates a second billing customer or subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionBillingService billingService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, billingService, httpContext.User, httpContext.RequestAborted);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status502BadGateway)
            .WithTags(SubscriptionEndpointMappings.TAG);
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService) =>
        HandleAsync(request, billingService, new ClaimsPrincipal(), CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService,
        ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userName = SubscriptionEndpointMappings.UserName(user);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await billingService.SubscribeAsync(userName, request.PlanHandle, cancellationToken);
            response.Message = result.Message;
            response.Subscription = result.Subscription == null ? null : SubscriptionEndpointMappings.ToDto(result.Subscription);

            switch (result.Status)
            {
                case SubscribeStatus.Created:
                    response.Status = "created";
                    return Results.Created("api/my-subscriptions", response);
                case SubscribeStatus.AlreadySubscribed:
                    response.Status = "already_subscribed";
                    return Results.Ok(response);
                case SubscribeStatus.InProgress:
                    response.Status = "in_progress";
                    return Results.Json(response, statusCode: StatusCodes.Status202Accepted);
                default:
                    response.Status = "unconfirmed";
                    return Results.Json(response, statusCode: StatusCodes.Status202Accepted);
            }
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionEndpointMappings.ToResult(ex, response.CorrelationId());
        }
    }
}
