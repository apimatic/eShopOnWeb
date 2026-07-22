using System;
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
/// UC2 — records metered usage against a subscription and reads back the period-to-date total.
/// Admin-guarded because it can target any subscription (mirrors <c>CreateCatalogItemEndpoint</c>'s
/// administrators-only guard).
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest request, ISubscriptionService subscriptionService) =>
            {
                request.SubscriptionId = subscriptionId;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var result = await subscriptionService.RecordUsageAsync(request.SubscriptionId, request.Quantity, request.Memo);
        return Results.Ok(new RecordUsageResponse(request.CorrelationId()) { Usage = result.ToDto() });
    }
}

public class RecordUsageRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public int Quantity { get; set; }
    public string? Memo { get; set; }
}

public class RecordUsageResponse : BaseResponse
{
    public RecordUsageResponse(Guid correlationId) : base(correlationId) { }

    public RecordUsageResponse() { }

    public UsageResultDto Usage { get; set; } = new();
}
