using System;
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

public class CreateSubscriptionRequest : AuthenticatedBillingRequest
{
    /// <summary>Handle of the plan to subscribe to, as listed by <c>GET /api/subscription-plans</c>.</summary>
    public string? PlanHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary><c>created</c>, <c>already_subscribed</c> or <c>pending_confirmation</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }

    /// <summary>The subscription (null while <c>pending_confirmation</c>).</summary>
    public SubscriptionDto? Subscription { get; set; }
}

/// <summary>
/// Subscribe the calling shopper to a plan. Idempotent per shopper: repeating the call never creates a second
/// Maxio customer or subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext context, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                request.UserName = context.User.Identity?.Name ?? string.Empty;
                request.RequestAborted = cancellationToken;
                return await HandleAsync(request, billing);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .Produces<BillingErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<BillingErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<BillingErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<BillingErrorResponse>(StatusCodes.Status502BadGateway)
            .Produces<BillingErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());
        try
        {
            var result = await billing.SubscribeAsync(request.UserName, request.PlanHandle, request.RequestAborted);
            response.Subscription = result.Subscription is null ? null : SubscriptionDto.From(result.Subscription);

            switch (result.Status)
            {
                case SubscribeStatus.Created:
                    response.Status = "created";
                    return Results.Created("api/my-subscriptions", response);
                case SubscribeStatus.AlreadySubscribed:
                    response.Status = "already_subscribed";
                    response.Message = "This account is already subscribed to this plan.";
                    return Results.Ok(response);
                default:
                    response.Status = "pending_confirmation";
                    response.Message = "The subscription request was sent but could not be confirmed yet. " +
                                       "Check GET /api/my-subscriptions shortly; repeating this request will not subscribe twice.";
                    return Results.Accepted("api/my-subscriptions", response);
            }
        }
        catch (BillingException ex)
        {
            return BillingResults.From(ex, request.CorrelationId());
        }
    }
}
