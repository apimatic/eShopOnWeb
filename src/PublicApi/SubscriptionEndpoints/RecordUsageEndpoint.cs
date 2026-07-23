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
/// Reports pay-as-you-go usage against a subscription and returns the running period-to-date total
/// (UC2). Administrator-only, because it may target any subscription.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest request, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;

                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var response = new RecordUsageResponse(request.CorrelationId());

        var report = await subscriptionService.RecordUsageAsync(request.SubscriptionId, request.Quantity, request.Memo);

        response.UsageId = report.Record.Id;
        response.SubscriptionId = report.Record.SubscriptionId;
        response.ComponentHandle = report.Record.ComponentHandle;
        response.Quantity = report.Record.Quantity;
        response.Memo = report.Record.Memo;
        response.IsPeriodToDateTotalAvailable = report.IsSummaryAvailable;
        response.PeriodToDateTotal = report.Summary?.UnitBalance;

        return Results.Ok(response);
    }
}
