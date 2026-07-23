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
/// Records pay-as-you-go usage against a subscription's metered component and returns the running
/// period-to-date total (plan.md UC2). A customer may only meter their own subscription; an administrator
/// may meter any.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest request, HttpContext http,
                ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.SetContext(subscriptionId, SubscriptionCaller.Restriction(http.User));
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var summary = await subscriptionService.RecordUsageAsync(
            request.SubscriptionId, request.Quantity, request.Memo, request.RestrictToUserReference, cancellationToken);

        return Results.Ok(new RecordUsageResponse(request.CorrelationId())
        {
            Usage = UsageSummaryDto.From(summary)
        });
    }
}
