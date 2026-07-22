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
/// Record pay-as-you-go usage against a subscription (UC2). Administrators may report usage for
/// any subscription; every other caller is confined to their own.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest request, ClaimsPrincipal user,
                ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.OwnerBuyerId = SubscriptionCaller.ResolveOwnerBuyerId(user);

                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request,
        ISubscriptionService subscriptionService)
    {
        var response = new RecordUsageResponse(request.CorrelationId());

        var report = await subscriptionService.RecordUsageAsync(request.SubscriptionId,
            request.OwnerBuyerId, request.Quantity, request.Memo);

        response.SubscriptionId = report.Recorded.SubscriptionId;
        response.ComponentHandle = report.Recorded.ComponentHandle;
        response.RecordedQuantity = report.Recorded.Quantity;
        response.Memo = report.Recorded.Memo;
        response.PeriodToDateTotal = report.PeriodToDateTotal;
        response.UnitPrice = report.UnitPrice;
        response.EstimatedPeriodToDateCharge = report.EstimatedPeriodToDateCharge;

        return Results.Ok(response);
    }
}
