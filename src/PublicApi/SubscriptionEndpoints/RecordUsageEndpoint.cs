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
/// Report pay-as-you-go usage against a subscription (UC2).
/// A customer may report against their own subscription; an administrator against any.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, RecordUsageRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.SubscriptionId = subscriptionId;

                return await HandleAsync(request, user.OwnershipScope(), subscriptionService, cancellationToken);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, null, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(RecordUsageRequest request, string? ownershipScope, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var receipt = await subscriptionService.RecordUsageAsync(ownershipScope, request.SubscriptionId, request.Quantity, request.Memo, cancellationToken);

        var response = new RecordUsageResponse(request.CorrelationId())
        {
            UsageId = receipt.Id,
            SubscriptionId = receipt.SubscriptionId,
            ComponentHandle = receipt.ComponentHandle,
            Quantity = receipt.Quantity,
            Memo = receipt.Memo,
            RecordedAt = receipt.RecordedAt,
            PeriodToDateTotal = receipt.PeriodToDateTotal
        };

        return Results.Ok(response);
    }
}
