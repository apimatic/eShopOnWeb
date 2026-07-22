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
/// Reports pay-as-you-go usage against a subscription's metered component (UC2)
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
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

        var usage = await subscriptionService.RecordUsageAsync(request.SubscriptionId, request.Quantity, request.Memo);

        response.SubscriptionId = usage.SubscriptionId;
        response.ComponentHandle = usage.ComponentHandle;
        response.UsageRecordId = usage.UsageRecordId;
        response.QuantityRecorded = usage.QuantityRecorded;
        response.Memo = usage.Memo;
        response.PeriodToDateUnits = usage.PeriodToDateUnits;
        response.PeriodToDateAmount = usage.PeriodToDateAmount;
        response.PeriodToDateAvailable = usage.PeriodToDateAvailable;

        return Results.Ok(response);
    }
}
