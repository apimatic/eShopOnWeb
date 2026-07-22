using System.Security.Claims;
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
/// Report metered usage against a subscription and read back the running period-to-date total
/// (UC2). Customers may report usage on their own subscription; administrators on any.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest request, ClaimsPrincipal user,
             ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.SubscriptionId = subscriptionId;
                request.User = user;
                request.CancellationToken = cancellationToken;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService) =>
        SubscriptionEndpointSupport.ExecuteAsync(async () =>
        {
            var denied = await SubscriptionEndpointSupport.EnsureCallerMayActOnAsync(
                request.User, request.SubscriptionId, subscriptionService, request.CancellationToken);

            if (denied is not null)
            {
                return denied;
            }

            var response = new RecordUsageResponse(request.CorrelationId());
            var receipt = await subscriptionService.RecordUsageAsync(
                request.SubscriptionId, request.Quantity, request.Memo, request.CancellationToken);

            response.Usage = SubscriptionEndpointSupport.ToDto(receipt);

            return Results.Ok(response);
        });
}
