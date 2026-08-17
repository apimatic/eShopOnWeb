using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — lists the provider's own record of
/// messages from the configured sending number over the range and lines them up against what eShop believes
/// it sent, surfacing either-side discrepancies. Administrator only. <c>from</c>/<c>to</c> are ISO-8601.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                DateTimeOffset from,
                DateTimeOffset to,
                IOrderNotificationService orderNotificationService,
                CancellationToken cancellationToken) =>
            {
                if (to < from)
                {
                    return Results.BadRequest(new { error = "'to' must not be earlier than 'from'." });
                }

                var report = await orderNotificationService.ReconcileAsync(from, to, cancellationToken);

                var response = new ReconciliationResponse
                {
                    FromUtc = report.FromUtc,
                    ToUtc = report.ToUtc,
                    FromNumber = report.FromNumber,
                    ProviderCount = report.ProviderCount,
                    EShopCount = report.EShopCount,
                    InBothCount = report.InBothCount,
                    ProviderOnlyCount = report.ProviderOnlyCount,
                    EShopOnlyCount = report.EShopOnlyCount,
                    Entries = report.Entries.Select(e => new ReconciliationEntryDto
                    {
                        Match = e.Match.ToString(),
                        ProviderMessageSid = e.ProviderMessageSid,
                        ProviderStatus = e.ProviderStatus,
                        ProviderDateSent = e.ProviderDateSent,
                        NotificationId = e.NotificationId,
                        OrderId = e.OrderId,
                        EShopStatus = e.EShopStatus
                    }).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }
}
