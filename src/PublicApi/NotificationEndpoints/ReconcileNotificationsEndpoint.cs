using System;
using System.Collections.Generic;
using System.Globalization;
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
    public string? NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string Match { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? ApplicationStatus { get; set; }
    public string? DateSent { get; set; }
    public string? Kind { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOrderNotificationService orders) =>
            {
                return await HandleAsync(from, to, orders);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService orders)
        => HandleAsync(string.Empty, string.Empty, orders);

    public async Task<IResult> HandleAsync(string from, string to, IOrderNotificationService orders)
    {
        if (!TryParseIso(from, out var fromUtc) || !TryParseIso(to, out var toUtc))
        {
            return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
        }

        if (fromUtc > toUtc)
        {
            return Results.BadRequest(new { message = "from must be earlier than or equal to to." });
        }

        var report = await orders.ReconcileAsync(fromUtc, toUtc);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                NotificationId = e.NotificationId,
                ProviderSid = e.ProviderSid,
                Match = e.Match,
                ProviderStatus = e.ProviderStatus,
                ApplicationStatus = e.ApplicationStatus,
                DateSent = e.DateSent,
                Kind = e.Kind
            }).ToList()
        });
    }

    private static bool TryParseIso(string value, out DateTimeOffset parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out parsed);
    }
}
