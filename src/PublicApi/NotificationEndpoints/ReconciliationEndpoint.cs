using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? LocalStatus { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> MissingLocally { get; set; } = new();
    public List<ReconciliationEntryDto> MissingAtProvider { get; set; } = new();
}

/// <summary>
/// Operator action: lines up the provider's own record of messages sent from this
/// application's sending number for a date range against what eShop believes it sent.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(from, to, notificationService);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notificationService)
    {
        if (to < from)
        {
            return Results.BadRequest(new { error = "to must not be before from" });
        }

        var report = await notificationService.ReconcileAsync(from, to);

        static ReconciliationEntryDto ToDto(ReconciliationEntry e) => new()
        {
            ProviderMessageSid = e.ProviderMessageSid,
            NotificationId = e.NotificationId,
            LocalStatus = e.LocalStatus,
            ProviderStatus = e.ProviderStatus,
            DateSent = e.DateSent
        };

        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(ToDto).ToList(),
            MissingLocally = report.MissingLocally.Select(ToDto).ToList(),
            MissingAtProvider = report.MissingAtProvider.Select(ToDto).ToList()
        });
    }
}
