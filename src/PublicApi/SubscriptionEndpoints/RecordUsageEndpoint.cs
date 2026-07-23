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
/// Record pay-as-you-go usage against a subscription (UC2).
/// </summary>
/// <remarks>
/// A customer may only report usage on their own subscription; administrators may report it on any.
/// The response also carries the running period-to-date total, or marks it unavailable if the provider
/// could not be read back.
/// </remarks>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/usages",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest request, ClaimsPrincipal user,
                ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, user, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");

        app.MapGet("api/subscriptions/{subscriptionId}/usages",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                var response = new RecordUsageResponse();
                var summary = await subscriptionService.GetUsageSummaryAsync(user.ToActor(), subscriptionId);
                response.Usage = summary.ToDto();
                return Results.Ok(response);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        RecordUsageRequest request,
        ClaimsPrincipal user,
        ISubscriptionService subscriptionService)
    {
        var response = new RecordUsageResponse(request.CorrelationId());

        var report = await subscriptionService.RecordUsageAsync(
            user.ToActor(), request.SubscriptionId, request.Quantity, request.Memo);

        response.Usage = report.ToDto();

        return Results.Ok(response);
    }
}
