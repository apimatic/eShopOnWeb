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
/// Records metered usage against any subscription (UC2, admin surface).
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        // Admin-guarded because this acts on any subscription, not only the caller's own.
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, RecordUsageRequest request, ISubscriptionService subscriptionService) =>
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

        var result = await subscriptionService.RecordUsageForSubscriptionAsync(
            request.SubscriptionId, request.Quantity, request.Memo);

        response.Usage = UsageRecordDto.FromResult(result);

        return Results.Ok(response);
    }
}
