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
/// Previews the cost of moving a subscription to another plan, without applying it (UC3, step 2).
/// </summary>
public class PlanChangePreviewEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change/preview",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int subscriptionId,
                PlanChangeRequest request,
                ClaimsPrincipal user,
                ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                request.Bind(subscriptionId, user, cancellationToken);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangePreviewResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
        {
            return Results.BadRequest("A target plan handle is required.");
        }

        if (!request.TryParseTiming(out var timing))
        {
            return Results.BadRequest("Timing must be either 'Immediate' or 'AtNextRenewal'.");
        }

        var actor = SubscriptionActorResolver.Resolve(request.User);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var response = new PlanChangePreviewResponse(request.CorrelationId());

        var preview = await subscriptionService.PreviewPlanChangeAsync(
            actor,
            request.SubscriptionId,
            request.TargetPlanHandle,
            timing,
            request.CancellationToken);

        response.Preview = PlanChangePreviewDto.FromPreview(preview);

        return Results.Ok(response);
    }
}

/// <summary>
/// Commits a plan change (UC3, step 4). Echo back the previewed
/// <see cref="PlanChangeRequest.ExpectedPaymentDueInCents"/> to guarantee the change is applied at
/// the amount the customer confirmed, or refused as stale.
/// </summary>
public class PlanChangeCommitEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int subscriptionId,
                PlanChangeRequest request,
                ClaimsPrincipal user,
                ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                request.Bind(subscriptionId, user, cancellationToken);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.TargetPlanHandle))
        {
            return Results.BadRequest("A target plan handle is required.");
        }

        if (!request.TryParseTiming(out var timing))
        {
            return Results.BadRequest("Timing must be either 'Immediate' or 'AtNextRenewal'.");
        }

        var actor = SubscriptionActorResolver.Resolve(request.User);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var response = new PlanChangeResponse(request.CorrelationId());

        var subscription = await subscriptionService.ChangePlanAsync(
            actor,
            request.SubscriptionId,
            request.TargetPlanHandle,
            timing,
            request.ExpectedPaymentDueInCents,
            request.CancellationToken);

        response.Subscription = SubscriptionDto.FromSubscription(subscription);

        return Results.Ok(response);
    }
}

public class PlanChangeRequest : BaseRequest
{
    /// <summary>The stable handle of the plan to move to.</summary>
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary><c>Immediate</c> (prorated) or <c>AtNextRenewal</c> (not prorated).</summary>
    public string Timing { get; set; } = nameof(PlanChangeTiming.Immediate);

    /// <summary>
    /// The previewed amount the customer confirmed, in cents. When supplied on the commit call,
    /// the change is refused if the provider would now charge something different.
    /// </summary>
    public long? ExpectedPaymentDueInCents { get; set; }

    internal int SubscriptionId { get; private set; }

    internal ClaimsPrincipal? User { get; private set; }

    internal CancellationToken CancellationToken { get; private set; }

    internal void Bind(int subscriptionId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        SubscriptionId = subscriptionId;
        User = user;
        CancellationToken = cancellationToken;
    }

    internal bool TryParseTiming(out PlanChangeTiming timing) =>
        Enum.TryParse(Timing, ignoreCase: true, out timing) && Enum.IsDefined(timing);
}

public class PlanChangePreviewResponse : BaseResponse
{
    public PlanChangePreviewResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlanChangePreviewResponse()
    {
    }

    public PlanChangePreviewDto? Preview { get; set; }
}

public class PlanChangeResponse : BaseResponse
{
    public PlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlanChangeResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}
