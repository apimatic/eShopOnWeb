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
/// UC2: records usage of the metered "api-call" component against a subscription. Customers may only
/// record against their own subscription; admins (Administrators role) may record against any (§4.1).
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, SubscriptionEndpointContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, SubscriptionEndpointContext.From(subscriptionService, user));
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, SubscriptionEndpointContext context)
    {
        var response = new RecordUsageResponse(request.CorrelationId());

        var result = await context.SubscriptionService.RecordUsageAsync(
            context.UserId, request.SubscriptionId, request.Quantity, request.Memo, context.IsAdmin);

        response.QuantityRecorded = result.QuantityRecorded;
        response.Memo = result.Memo;
        response.RecordedAt = result.RecordedAt;
        response.PeriodToDateQuantity = result.PeriodToDateQuantity;

        return Results.Ok(response);
    }
}
