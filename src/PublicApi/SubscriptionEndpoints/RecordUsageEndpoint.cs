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
/// Records pay-as-you-go usage against a subscription (UC2).
/// </summary>
/// <remarks>
/// A customer may report usage on their own subscription; an administrator may report it on any.
/// </remarks>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest request, ClaimsPrincipal user,
             ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.Bind(subscriptionId, user.ResolveActingScope(), cancellationToken);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var response = new RecordUsageResponse(request.CorrelationId());

        var report = await subscriptionService.RecordUsageAsync(
            request.SubscriptionId,
            request.Quantity,
            request.Memo,
            request.ActingUserReference,
            request.CancellationToken);

        response.Usage = report.ToDto();

        return Results.Ok(response);
    }
}
