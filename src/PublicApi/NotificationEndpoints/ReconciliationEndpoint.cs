using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: lists the provider's own record of messages sent from the configured sending number
/// over a date range and lines them up against what eShop believes it sent, so a message the provider
/// knows about and eShop doesn't — or the reverse — is visible.
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
            (DateTimeOffset from, DateTimeOffset to) => await HandleAsync(from, to))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
            return Results.BadRequest(new { error = "'to' must be on or after 'from'." });

        var report = await _notificationService.ReconcileAsync(from.ToUniversalTime(), to.ToUniversalTime());

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = PhoneMask.Mask(report.FromNumber),
            ProviderMessageCount = report.ProviderMessageCount,
            EShopNotificationCount = report.EShopNotificationCount,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            EShopOnly = report.EShopOnly.Select(ToDto).ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciliationEntryDto ToDto(ReconciliationEntry e) => new()
    {
        ProviderSid = e.ProviderSid,
        ProviderStatus = e.ProviderStatus,
        DateSent = e.DateSent,
        NotificationId = e.NotificationId,
        OrderId = e.OrderId,
        NotificationType = e.NotificationType
    };
}
