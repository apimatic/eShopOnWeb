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
/// Records pay-as-you-go usage against a subscription (UC2). A customer may report usage on their
/// own subscription; an administrator may report it on any.
/// </summary>
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
                request.Bind(subscriptionId, user, cancellationToken);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        // Rejected before any provider call: a non-positive quantity is never billable.
        if (request.Quantity <= 0m)
        {
            return Results.BadRequest("Quantity must be greater than zero.");
        }

        var actor = SubscriptionActorResolver.Resolve(request.User);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var response = new RecordUsageResponse(request.CorrelationId());

        var report = await subscriptionService.RecordUsageAsync(
            actor,
            request.SubscriptionId,
            request.Quantity,
            request.Memo,
            request.CancellationToken);

        response.Usage = UsageReportDto.FromReport(report);

        return Results.Ok(response);
    }
}

public class RecordUsageRequest : BaseRequest
{
    /// <summary>How many units were consumed. Must be greater than zero.</summary>
    public decimal Quantity { get; set; }

    /// <summary>An optional note stored alongside the usage record.</summary>
    public string? Memo { get; set; }

    internal int SubscriptionId { get; private set; }

    internal ClaimsPrincipal? User { get; private set; }

    internal CancellationToken CancellationToken { get; private set; }

    internal void Bind(int subscriptionId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        SubscriptionId = subscriptionId;
        User = user;
        CancellationToken = cancellationToken;
    }
}

public class RecordUsageResponse : BaseResponse
{
    public RecordUsageResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RecordUsageResponse()
    {
    }

    public UsageReportDto? Usage { get; set; }
}
