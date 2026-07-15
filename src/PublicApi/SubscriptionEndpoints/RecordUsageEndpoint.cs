using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
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
/// Records pay-as-you-go usage against a subscription's metered component (UC2). A
/// customer may only report usage for their own subscription; an administrator may
/// report usage for any subscription (ownership is enforced by SubscriptionService).
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RecordUsageRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.UserId = user.Identity!.Name!;
                request.IsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var response = new RecordUsageResponse(request.CorrelationId());

        var usage = await subscriptionService.RecordUsageAsync(request.UserId, request.IsAdmin, request.SubscriptionId, request.Quantity, request.Memo);

        response.UsageId = usage.UsageId;
        response.Quantity = usage.Quantity;
        response.Memo = usage.Memo;
        response.UnitBalance = usage.UnitBalance;

        return Results.Ok(response);
    }
}

public class RecordUsageRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public double Quantity { get; set; }
    public string? Memo { get; set; }

    [JsonIgnore]
    public string UserId { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsAdmin { get; set; }
}

public class RecordUsageResponse : BaseResponse
{
    public RecordUsageResponse(Guid correlationId) : base(correlationId)
    {
    }

    public long UsageId { get; set; }
    public double Quantity { get; set; }
    public string? Memo { get; set; }
    public int? UnitBalance { get; set; }
}
