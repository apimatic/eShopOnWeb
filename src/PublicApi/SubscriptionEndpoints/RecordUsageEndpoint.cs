using System;
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
/// UC2 — records metered usage. Customers record usage on their own subscription; admins
/// (Roles=Administrators) may target any subscription.
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
                return await HandleAsync(request, new SubscriptionEndpointContext(subscriptionService, user));
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, SubscriptionEndpointContext context)
    {
        var response = new RecordUsageResponse(request.CorrelationId());
        var ownerReference = SubscriptionEndpointHelpers.ResolveOwnerReference(context.User);

        var usage = await context.SubscriptionService.RecordUsageAsync(
            ownerReference, request.SubscriptionId, request.Quantity, request.Memo);

        response.Usage = SubscriptionDtoMapper.ToDto(usage);
        return Results.Ok(response);
    }
}

public class RecordUsageRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public double Quantity { get; set; }
    public string? Memo { get; set; }
}

public class RecordUsageResponse : BaseResponse
{
    public RecordUsageResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RecordUsageResponse()
    {
    }

    public UsageRecordDto? Usage { get; set; }
}
