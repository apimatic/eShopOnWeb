using System;
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
/// Record pay-as-you-go usage against a subscription (UC2).
/// </summary>
/// <remarks>
/// A customer may only meter their own subscription; reporting against any subscription requires
/// the Administrators role.
/// </remarks>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int subscriptionId,
                RecordUsageRequest request,
                ClaimsPrincipal user,
                ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                request.SubscriptionId = subscriptionId;

                if (!await SubscriptionAuthorization.CanActOnSubscriptionAsync(
                        user, subscriptionId, subscriptionService, cancellationToken))
                {
                    return SubscriptionCaller.Forbidden();
                }

                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        RecordUsageRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var result = await subscriptionService.RecordUsageAsync(
            request.SubscriptionId, request.Quantity, request.Memo, cancellationToken);

        var response = new RecordUsageResponse(request.CorrelationId())
        {
            Usage = UsageRecordDto.From(result)
        };

        return Results.Ok(response);
    }
}

public class RecordUsageRequest : BaseRequest
{
    /// <summary>Taken from the route; any value in the body is overwritten.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Units consumed. Must be greater than zero.</summary>
    public decimal Quantity { get; set; }

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
