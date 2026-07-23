using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Records pay-as-you-go usage against a subscription's metered component (UC2). A customer may
/// report usage on their own subscription; an administrator may report it on any subscription.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId,
             RecordUsageRequest request,
             ClaimsPrincipal user,
             ISubscriptionService subscriptionService,
             CancellationToken cancellationToken) =>
            {
                request.SubscriptionId = subscriptionId;
                request.UserReference = SubscriptionUser.ReferenceOf(user);
                request.IsAdministrator = SubscriptionUser.IsAdministrator(user);
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        return HandleAsync(request, subscriptionService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var report = request.IsAdministrator
            ? await subscriptionService.RecordUsageForAnyCustomerAsync(request.SubscriptionId,
                request.Quantity, request.Memo, cancellationToken)
            : await subscriptionService.RecordUsageAsync(request.UserReference, request.SubscriptionId,
                request.Quantity, request.Memo, cancellationToken);

        return Results.Ok(ToResponse(request.CorrelationId(), report));
    }

    private static RecordUsageResponse ToResponse(System.Guid correlationId, UsageReport report)
    {
        return new RecordUsageResponse(correlationId)
        {
            SubscriptionId = report.SubscriptionId,
            RecordedQuantity = report.Recorded.Quantity,
            Usage = report.Usage is null ? null : UsageDto.FromSummary(report.Usage),
            IsTotalAvailable = report.IsTotalAvailable,
            Message = report.IsTotalAvailable
                ? "The recorded usage will appear on your next renewal invoice."
                : "The usage was recorded and will appear on your next renewal invoice; " +
                  "the running total is temporarily unavailable."
        };
    }
}
