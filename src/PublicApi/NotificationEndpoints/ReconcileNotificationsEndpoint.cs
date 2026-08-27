using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconcileNotificationsResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
}

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, ClaimsPrincipal>
{
    private readonly IOrderNotificationService _notifications;

    public ReconcileNotificationsEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, ClaimsPrincipal user) =>
                await HandleAsync(new ReconcileNotificationsRequest { From = from, To = to }, user))
            .Produces<ReconcileNotificationsResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, ClaimsPrincipal user)
    {
        _ = user;
        if (request.From == default || request.To == default)
        {
            return Results.BadRequest(new { message = "Both from and to must be ISO-8601 date-times." });
        }

        if (request.From > request.To)
        {
            return Results.BadRequest(new { message = "from must be earlier than or equal to to." });
        }

        var report = await _notifications.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconcileNotificationsResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            EShopOnly = report.EShopOnly.Select(ToDto).ToList()
        });
    }

    private static ReconciliationEntryDto ToDto(ReconciliationEntry entry)
    {
        return new ReconciliationEntryDto
        {
            ProviderMessageSid = entry.ProviderMessageSid,
            NotificationId = entry.NotificationId,
            ProviderStatus = entry.ProviderStatus,
            EShopStatus = entry.EShopStatus,
            DateSent = entry.DateSent
        };
    }
}
