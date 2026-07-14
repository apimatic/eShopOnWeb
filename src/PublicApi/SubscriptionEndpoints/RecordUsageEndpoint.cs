using System.Security.Claims;
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
/// [Authorize] Records metered usage (UC2): against the caller's own active subscription by
/// default, or an explicit subscription when the caller is an Administrator ("any subscription").
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RecordUsageRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = await SubscriptionEndpointHelpers.ResolveSubscriptionIdAsync(subscriptionService, user, request.SubscriptionId);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var response = new RecordUsageResponse(request.CorrelationId());
        var balance = await subscriptionService.RecordUsageAsync(request.SubscriptionId!.Value, request.Quantity, request.Memo);
        response.SubscriptionId = balance.SubscriptionId;
        response.RecordedQuantity = balance.RecordedQuantity;
        response.PeriodToDateUnitBalance = balance.PeriodToDateUnitBalance;
        return Results.Ok(response);
    }
}
