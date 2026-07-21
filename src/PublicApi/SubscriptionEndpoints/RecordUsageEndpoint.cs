using System.Security.Claims;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// UC2 — records one usage report against a subscription's metered component. Customers may
/// only report against their own subscription; Administrators may report against any.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RecordUsageRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.UserName = user.Identity?.Name ?? string.Empty;
                request.IsAdministrator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        Guard.Against.NullOrEmpty(request.UserName, nameof(request.UserName));

        if (!await SubscriptionAccessControl.CanAccessAsync(subscriptionService, request.UserName, request.IsAdministrator, request.SubscriptionId))
        {
            return Results.Forbid();
        }

        var response = new RecordUsageResponse(request.CorrelationId());

        var result = await subscriptionService.RecordUsageAsync(request.SubscriptionId, request.Quantity, request.Memo);
        response.UsageId = result.UsageId;
        response.QuantityRecorded = result.QuantityRecorded;
        response.PeriodToDateUnits = result.PeriodToDateUnits;
        response.PeriodToDateAvailable = result.PeriodToDateAvailable;

        return Results.Ok(response);
    }
}
