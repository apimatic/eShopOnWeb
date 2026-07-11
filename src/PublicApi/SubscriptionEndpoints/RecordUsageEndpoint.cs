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
/// UC2: records usage against a subscription's metered component and reads back the period-to-date total.
/// Any authenticated user may record usage against their own subscription; administrators may target any
/// subscription (ownership is enforced by <see cref="ISubscriptionService"/>, not by role alone).
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageBody body, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                var isAdmin = user.IsInRole(Constants.Roles.ADMINISTRATORS);
                var request = new RecordUsageRequest(user.Identity!.Name!, isAdmin, subscriptionId, body.Quantity, body.Memo);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var response = new RecordUsageResponse(request.CorrelationId());

        var (usage, summary) = await subscriptionService.RecordUsageAsync(
            request.ActingBuyerId, request.IsAdmin, request.SubscriptionId, request.Quantity, request.Memo);

        response.Usage = UsageRecordDto.FromDomain(usage);
        response.Summary = UsagePeriodSummaryDto.FromDomain(summary);

        return Results.Ok(response);
    }
}
