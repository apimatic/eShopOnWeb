using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntryDto
{
    public string? MessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? DateSent { get; set; }

    /// <summary>Matched, ProviderOnly (provider knows it, eShop doesn't) or LocalOnly.</summary>
    public string Match { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// Operator action: lines the provider's own record of messages sent from
/// this application's sending number in [from, to] up against what eShop
/// believes it sent. Both directions of drift are visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to,
             IOrderNotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(from, to, notificationService, cancellationToken);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset? from, DateTimeOffset? to,
        IOrderNotificationService notificationService, CancellationToken cancellationToken)
    {
        if (from is null || to is null)
        {
            return Results.BadRequest(new { error = "from and to (ISO-8601 date-times) are required." });
        }
        if (from > to)
        {
            return Results.BadRequest(new { error = "from must not be after to." });
        }

        try
        {
            var report = await notificationService.ReconcileAsync(from.Value, to.Value, cancellationToken);
            return Results.Ok(new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                ProviderMessageCount = report.ProviderMessageCount,
                LocalNotificationCount = report.LocalNotificationCount,
                Entries = report.Entries.Select(e => new ReconciliationEntryDto
                {
                    MessageSid = e.MessageSid,
                    NotificationId = e.NotificationId,
                    Status = e.Status,
                    DateSent = e.DateSent,
                    Match = e.Match
                }).ToList()
            });
        }
        catch (TextMessageProviderException)
        {
            return Results.Problem("The provider's message records could not be retrieved.", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
