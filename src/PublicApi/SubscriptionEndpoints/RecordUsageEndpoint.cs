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
/// Record metered usage against any subscription (administrators only)
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/usage",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RecordUsageRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var response = new RecordUsageResponse(request.CorrelationId());

        var report = await subscriptionService.RecordUsageForSubscriptionAsync(request.SubscriptionId,
            request.Quantity, request.Memo, CancellationToken.None);

        response.UsageId = report.RecordedUsage.Id;
        response.SubscriptionId = report.RecordedUsage.SubscriptionId;
        response.ComponentHandle = report.RecordedUsage.ComponentHandle;
        response.Quantity = report.RecordedUsage.Quantity;
        response.Memo = report.RecordedUsage.Memo;
        response.PeriodToDateTotal = report.PeriodToDateTotal;

        return Results.Ok(response);
    }
}
