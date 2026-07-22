using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Pauses, resumes, cancels, or reactivates a subscription (UC4)
/// </summary>
public class LifecycleEndpoint : IEndpoint<IResult, LifecycleRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/lifecycle",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (LifecycleRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                request.UserReference = user.Identity?.Name ?? string.Empty;
                // Acting on someone else's subscription is an administrative act.
                request.IsAdministrator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<LifecycleResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(LifecycleRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(LifecycleRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserReference))
        {
            return Results.Unauthorized();
        }

        if (!Enum.TryParse<SubscriptionLifecycleAction>(request.Action, true, out var action))
        {
            return Results.BadRequest(
                "Action must be one of Pause, Resume, Cancel, or Reactivate.");
        }

        var timing = CancellationTiming.Immediate;
        if (!string.IsNullOrWhiteSpace(request.CancellationTiming) &&
            !Enum.TryParse(request.CancellationTiming, true, out timing))
        {
            return Results.BadRequest(
                $"Cancellation timing must be '{nameof(CancellationTiming.Immediate)}' or " +
                $"'{nameof(CancellationTiming.EndOfPeriod)}'.");
        }

        if (request.SubscriptionId.HasValue && !request.IsAdministrator)
        {
            // An explicit status, not Results.Forbid(): the host's default forbid handler is
            // Identity's cookie scheme, which would answer an API caller with a login redirect.
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var response = new LifecycleResponse(request.CorrelationId());

        var subscription = request.SubscriptionId.HasValue
            ? await subscriptionService.ExecuteLifecycleActionForSubscriptionAsync(request.SubscriptionId.Value,
                action, timing, request.Reason, cancellationToken)
            : await subscriptionService.ExecuteLifecycleActionAsync(request.UserReference, action, timing,
                request.Reason, cancellationToken);

        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }
}
