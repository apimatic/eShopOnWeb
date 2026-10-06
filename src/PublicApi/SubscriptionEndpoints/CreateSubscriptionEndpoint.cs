using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling shopper to a plan. Idempotent per shopper: a repeated request for the same plan
/// returns the existing subscription (200) instead of creating a second one (201).
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>()
            .Produces<ErrorDetails>(StatusCodes.Status400BadRequest)
            .Produces<ErrorDetails>(StatusCodes.Status409Conflict)
            .Produces<ErrorDetails>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ErrorDetails>(StatusCodes.Status504GatewayTimeout)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
        HandleAsync(request, user, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user,
        ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            // Same body shape as ExceptionMiddleware writes for every other error.
            return Results.Content(new ErrorDetails
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                Message = "planHandle is required."
            }.ToString(), "application/json", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await billingService.SubscribeAsync(buyerId, request.PlanHandle, cancellationToken);

        response.Subscription = SubscriptionDto.From(result.Subscription);
        response.Created = result.Created;

        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
