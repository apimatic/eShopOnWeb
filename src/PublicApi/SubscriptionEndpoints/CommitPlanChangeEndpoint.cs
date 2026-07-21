using System;
using System.Security.Claims;
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

public class CommitPlanChangeRequest : BaseRequest
{
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary>"Now" (prorated) or "AtRenewal" (no proration).</summary>
    public PlanChangeTiming Timing { get; set; }

    /// <summary>
    /// The exact preview the customer confirmed (required when <see cref="Timing"/> is "Now") - the
    /// commit is rejected if a freshly recomputed preview no longer matches these amounts (UC3).
    /// </summary>
    public PlanChangePreviewDto? ConfirmedPreview { get; set; }

    internal int SubscriptionId { get; set; }
    internal string CustomerReference { get; set; } = string.Empty;
    internal bool IsAdmin { get; set; }
}

public class CommitPlanChangeResponse : BaseResponse
{
    public CommitPlanChangeResponse(Guid correlationId) : base(correlationId) { }
    public CommitPlanChangeResponse() { }

    public SubscriptionDto Subscription { get; set; } = null!;
}

/// <summary>Commits a plan change with the chosen timing (UC3).</summary>
public class CommitPlanChangeEndpoint : IEndpoint<IResult, CommitPlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int subscriptionId, CommitPlanChangeRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                request.SubscriptionId = subscriptionId;
                request.CustomerReference = user.FindFirstValue(ClaimTypes.Name)!;
                request.IsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CommitPlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CommitPlanChangeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new CommitPlanChangeResponse(request.CorrelationId());

        var subscription = await subscriptionService.CommitPlanChangeAsync(
            request.CustomerReference,
            request.SubscriptionId,
            request.TargetPlanHandle,
            request.Timing,
            request.ConfirmedPreview?.ToDomain(),
            request.IsAdmin);

        response.Subscription = SubscriptionDto.FromDomain(subscription);

        return Results.Ok(response);
    }
}
