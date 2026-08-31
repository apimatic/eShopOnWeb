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
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Reconciliation report (operator): the provider's own record of messages sent from this
/// application's configured sending number over a date range, lined up against what eShop
/// believes it sent.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset>
{
    private readonly IOrderNotificationService _notificationService;

    public ReconciliationEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(from, to);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to)
    {
        // The provider calls carry their own time budget inside the service.
        var report = await _notificationService.ReconcileAsync(from, to, CancellationToken.None);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            ProviderMessageCount = report.ProviderMessageCount,
            LocalNotificationCount = report.LocalNotificationCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                ProviderMessageSid = e.ProviderMessageSid,
                LocalNotificationId = e.LocalNotificationId,
                LocalOrderId = e.LocalOrderId,
                To = e.To,
                ProviderStatus = e.ProviderStatus,
                LocalStatus = e.LocalStatus,
                DateSent = e.DateSent,
                Disposition = ToWireValue(e.Disposition)
            }).ToList()
        };
        return Results.Ok(response);
    }

    private static string ToWireValue(ReconciliationDisposition disposition) => disposition switch
    {
        ReconciliationDisposition.Matched => "matched",
        ReconciliationDisposition.MissingLocally => "missing-locally",
        ReconciliationDisposition.MissingAtProvider => "missing-at-provider",
        _ => disposition.ToString()
    };
}
