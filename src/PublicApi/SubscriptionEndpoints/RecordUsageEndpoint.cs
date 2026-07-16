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
/// UC2: report pay-as-you-go usage against a subscription's metered component. A customer may only record
/// usage against their own subscription; an Administrator may record usage against any subscription.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest request, ClaimsPrincipal principal, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                request.CustomerReference = principal.Identity!.Name!;
                request.IsAdmin = principal.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var response = new RecordUsageResponse(request.CorrelationId());

        var usage = await subscriptionService.RecordUsageAsync(
            request.CustomerReference, request.SubscriptionId, request.Quantity, request.Memo, request.IsAdmin);

        response.Usage = usage;

        return Results.Ok(response);
    }
}
