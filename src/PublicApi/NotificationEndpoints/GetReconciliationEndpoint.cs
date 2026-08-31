using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: lines up the provider's own record of messages for a date range
/// (this application's sending number only) against what eShop believes it sent.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(
                    new GetReconciliationRequest(from, to) { CancellationToken = cancellationToken },
                    notificationService);
            })
            .Produces<GetReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, IOrderNotificationService notificationService)
    {
        if (request.To <= request.From)
        {
            return Results.BadRequest(new GetReconciliationResponse(request.CorrelationId())
            {
                Error = "The 'to' date-time must be after the 'from' date-time (both ISO-8601)."
            });
        }

        var report = await notificationService.GetReconciliationAsync(request.From, request.To, request.CancellationToken);

        return Results.Ok(new GetReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Truncated = report.Truncated,
            ProviderMessageCount = report.ProviderMessageCount,
            AppNotificationCount = report.AppNotificationCount,
            Matched = report.Matched,
            ProviderOnly = report.ProviderOnly,
            AppOnly = report.AppOnly
        });
    }
}
