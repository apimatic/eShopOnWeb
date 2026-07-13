using System.Threading.Tasks;
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
/// UC2: record usage against the configured metered component on a subscription. A customer may
/// only report usage for their own subscription; an Administrator may report for any subscription.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, RecordUsageBody body, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                var customerReference = httpContext.User.Identity?.Name ?? string.Empty;
                var actingAsAdmin = httpContext.User.IsInRole(Constants.Roles.ADMINISTRATORS);
                var request = new RecordUsageRequest(subscriptionId, customerReference, actingAsAdmin, body);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var response = new RecordUsageResponse(request.CorrelationId());

        var result = await subscriptionService.RecordUsageAsync(
            request.CustomerReference, request.ActingAsAdmin, request.SubscriptionId, request.Quantity, request.Memo);

        response.UsageId = result.UsageId;
        response.QuantityRecorded = result.QuantityRecorded;
        response.PeriodToDateTotal = result.PeriodToDateTotal;
        return Results.Ok(response);
    }
}
