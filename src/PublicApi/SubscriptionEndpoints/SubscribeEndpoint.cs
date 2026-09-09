using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: ensures a Maxio customer exists for the
/// shopper and, if they already have a live subscription to the plan, returns it rather than creating a
/// duplicate. The caller's identity comes from the JWT, never from the request body.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                request.CallerUserName = user.Identity?.Name;
                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Subscribes the caller to a plan")
            {
                Description = "Ensures a billing customer exists for the caller and subscribes them to the given plan handle. Idempotent."
            });
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billingService)
        => HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        SubscribeRequest request,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.CallerUserName))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.BadRequest(new { message = "planHandle is required." });

        var subscriber = SubscriberIdentity.FromUser(request.CallerUserName!);

        try
        {
            var result = await billingService.SubscribeAsync(subscriber, request.PlanHandle!, cancellationToken);

            response.Subscription = result.Subscription.ToDto();
            response.AlreadySubscribed = result.AlreadyExisted;

            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"api/my-subscriptions", response);
        }
        catch (PlanNotFoundException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
        catch (SubscriptionBillingException ex)
        {
            // Upstream billing error (e.g. Maxio rejected the request or is unavailable).
            return Results.Problem(
                title: "Subscription could not be completed.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
