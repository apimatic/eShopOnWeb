using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
/// </summary>
/// <remarks>
/// Idempotent by design: the billing customer is looked up by a stable reference before it is created, and an
/// existing subscription to the same plan is returned instead of a second one being created — so a
/// double-click never produces two customers or two subscriptions.
/// </remarks>
public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscribeCommand, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
             ClaimsPrincipal user,
             ISubscriptionBillingService billingService,
             CancellationToken cancellationToken) =>
            {
                var subscriber = user.ToSubscriberIdentity(request.FirstName, request.LastName);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(
                    new SubscribeCommand(subscriber, request.PlanHandle), billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Subscribes the caller to a plan",
                description: "Ensures a billing customer exists for the caller and enrolls them in the requested plan. " +
                             "Repeating the request returns the existing subscription with alreadySubscribed set to true."));
    }

    public Task<IResult> HandleAsync(SubscribeCommand request, ISubscriptionBillingService billingService) =>
        HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        SubscribeCommand request,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            // Thrown rather than returned so every failure on this endpoint has the one error shape the
            // exception middleware produces.
            throw new BillingException("A planHandle is required.", StatusCodes.Status400BadRequest);
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await billingService.SubscribeAsync(
            request.Subscriber, request.PlanHandle, cancellationToken);

        response.Subscription = result.Subscription.ToDto();
        response.AlreadySubscribed = result.AlreadySubscribed;

        // A replayed request is not a new creation, so it answers 200 rather than 201.
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions/{result.Subscription.Id}", response);
    }
}
