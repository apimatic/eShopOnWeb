using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
/// <para>
/// The call is idempotent: the billing customer is created only if the shopper does not have one
/// yet, and a shopper who already holds a live subscription to the requested plan gets that
/// subscription back (HTTP 200) instead of a second one.
/// </para>
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
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Subscribe to a plan",
                description: "Enrolls the caller in the requested plan, creating their billing customer if needed. " +
                             "Repeating the call while a live subscription to the same plan exists returns that subscription with HTTP 200."));
    }

    /// <summary>
    /// Deliberately not cancellable: once enrollment starts, abandoning it midway could leave a
    /// subscription created in the billing system that the caller never learns about.
    /// </summary>
    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal user,
        ISubscriptionBillingService billingService)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(
                detail: "'planHandle' is required. Call GET /api/subscription-plans for the available handles.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid subscribe request");
        }

        if (!SubscriberFactory.TryCreate(user, out var subscriber, out var identityError))
        {
            return Results.Problem(
                detail: identityError,
                statusCode: StatusCodes.Status400BadRequest,
                title: "The caller cannot be identified");
        }

        var result = await billingService.SubscribeAsync(
            new SubscribeCommand(subscriber, request.PlanHandle.Trim(), request.IdempotencyKey));

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            Created = result.Created,
            Plan = result.Plan.ToDto()
        };

        return result.Created
            ? Results.Created("/api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
