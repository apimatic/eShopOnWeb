using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Commit a plan change with the chosen timing (UC3 step 4)
/// </summary>
public class PlanChangeEndpoint : IEndpoint<IResult, PlanChangeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/plan-change",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanChangeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.SubscriptionId = subscriptionId;

                return await HandleAsync(request, user.OwnershipScope(), subscriptionService, cancellationToken);
            })
            .Produces<PlanChangeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(PlanChangeRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, null, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(PlanChangeRequest request, string? ownershipScope, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var result = await subscriptionService.ChangePlanAsync(
            ownershipScope, request.SubscriptionId, request.TargetPlanHandle, request.ApplyImmediately, request.ConfirmedPaymentDue, cancellationToken);

        var response = new PlanChangeResponse(request.CorrelationId())
        {
            PreviousPlanHandle = result.PreviousPlanHandle,
            AppliedAmounts = PlanChangePreviewDto.From(result.Preview),
            EffectiveAt = result.EffectiveAt,
            Subscription = SubscriptionDto.From(result.Subscription)
        };

        return Results.Ok(response);
    }
}
