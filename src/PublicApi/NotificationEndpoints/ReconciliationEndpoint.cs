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
using Microsoft.eShopWeb.ApplicationCore.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntryDto
{
    public string? MessageSid { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? To { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public string Match { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int MissingLocallyCount { get; set; }
    public int MissingAtProviderCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// Reconciliation report (operator): the provider's own record of messages sent
/// from this application's configured sending number over [from, to), lined up
/// against what eShop believes it sent.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, IOrderNotificationService>
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
        if (from == default || to == default || from >= to)
        {
            return Results.BadRequest(new { message = "Query parameters 'from' and 'to' must be ISO-8601 date-times with from earlier than to." });
        }

        NotificationReconciliationReport report;
        try
        {
            report = await notificationService.ReconcileAsync(from, to);
        }
        catch (TwilioApiException)
        {
            return Results.Problem("The messaging provider could not be reached.", statusCode: StatusCodes.Status502BadGateway);
        }

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.MatchedCount,
            MissingLocallyCount = report.MissingLocallyCount,
            MissingAtProviderCount = report.MissingAtProviderCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                MessageSid = e.MessageSid,
                NotificationId = e.NotificationId,
                OrderId = e.OrderId,
                To = e.To,
                ProviderStatus = e.ProviderStatus,
                LocalStatus = e.LocalStatus,
                DateSent = e.DateSent,
                Match = e.Match.ToString()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
