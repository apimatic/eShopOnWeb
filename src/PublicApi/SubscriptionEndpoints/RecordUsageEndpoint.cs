using System.Security.Claims;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Records one pay-as-you-go usage report (UC2). A caller may only record usage against their own
/// active subscription unless they hold the Administrator role, in which case an explicit
/// <see cref="RecordUsageRequest.SubscriptionId"/> may target any subscription.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ClaimsPrincipal>
{
    private readonly ISubscriptionService _subscriptionService;

    public RecordUsageEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RecordUsageRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ClaimsPrincipal user)
    {
        Guard.Against.Null(user.Identity?.Name, nameof(user.Identity.Name));

        int subscriptionId;
        if (request.SubscriptionId.HasValue)
        {
            if (!user.IsInRole(Constants.Roles.ADMINISTRATORS))
            {
                return Results.Forbid();
            }

            subscriptionId = request.SubscriptionId.Value;
        }
        else
        {
            var active = await _subscriptionService.FindActiveSubscriptionAsync(user.Identity!.Name!);
            if (active == null)
            {
                return Results.BadRequest("No active subscription found for the current user.");
            }

            subscriptionId = active.Id;
        }

        var result = await _subscriptionService.RecordUsageAsync(subscriptionId, request.Quantity, request.Memo);

        var response = new RecordUsageResponse(request.CorrelationId())
        {
            UsageId = result.UsageId,
            Quantity = result.Quantity,
            PeriodToDateUnits = result.PeriodToDateUnits
        };
        return Results.Ok(response);
    }
}
