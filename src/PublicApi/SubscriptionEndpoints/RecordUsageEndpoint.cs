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
/// UC2: records a unit of metered (api-call) usage against a subscription. A customer may only record
/// usage on their own subscription; an Administrator may record it on any (enforced in
/// <c>SubscriptionService</c>, since only it knows the subscription's owning customer reference).
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, SubscriptionEndpointContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest request, ClaimsPrincipal principal, ISubscriptionService subscriptionService) =>
            {
                var userReference = principal.Identity?.Name;
                if (string.IsNullOrEmpty(userReference)) return Results.Unauthorized();

                request.SubscriptionId = subscriptionId;
                var context = new SubscriptionEndpointContext(subscriptionService, userReference, principal.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS));
                return await HandleAsync(request, context);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, SubscriptionEndpointContext context)
    {
        var response = new RecordUsageResponse(request.CorrelationId());

        var usage = await context.SubscriptionService.RecordUsageAsync(
            request.SubscriptionId,
            context.UserReference,
            context.IsAdmin,
            request.Quantity,
            request.Memo);

        response.Usage = usage;
        return Results.Ok(response);
    }
}
