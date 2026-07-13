using System.Security.Claims;
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
/// Records pay-as-you-go usage against a subscription's metered component (UC2). Customers
/// record usage for their own subscription; admins may record usage for any subscription.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest body, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                var request = new RecordUsageRequest(subscriptionId, body.Quantity, body.Memo,
                    user.Identity!.Name!, user.IsInRole(Constants.Roles.ADMINISTRATORS));
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var response = new RecordUsageResponse(request.CorrelationId());

        var result = await subscriptionService.RecordUsageAsync(request.UserReference, request.IsAdmin, request.SubscriptionId, request.Quantity, request.Memo);

        response.QuantityRecorded = result.QuantityRecorded;
        response.PeriodToDateTotal = result.PeriodToDateTotal;

        return Results.Ok(response);
    }
}
