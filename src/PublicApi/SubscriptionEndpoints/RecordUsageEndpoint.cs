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
/// Records pay-as-you-go metered usage against a subscription (UC2). Customers may only
/// record usage on their own subscription; Administrators may record it on any subscription.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest request, ISubscriptionService subscriptionService,
                HttpContext httpContext) =>
            {
                request.SubscriptionId = subscriptionId;
                request.OwnerReference = SubscriptionEndpointHelpers.ResolveOwnerReference(httpContext.User);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var response = new RecordUsageResponse(request.CorrelationId());

        var usage = await subscriptionService.RecordUsageAsync(request.OwnerReference, request.SubscriptionId,
            request.Quantity, request.Memo);

        response.Usage = new UsageRecordResultDto
        {
            UsageId = usage.UsageId,
            Quantity = usage.Quantity,
            RecordedAt = usage.RecordedAt,
            PeriodToDateBalance = usage.PeriodToDateBalance,
        };

        return Results.Ok(response);
    }
}
