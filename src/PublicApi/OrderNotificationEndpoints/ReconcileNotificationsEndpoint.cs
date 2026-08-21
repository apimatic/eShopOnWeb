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

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class ReconciliationReportResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> EshopOnly { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public string? BodyPreview { get; set; }
}

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, INotificationOperatorService service) => await HandleAsync(from, to, service))
            .Produces<ReconciliationReportResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(INotificationOperatorService service) =>
        Task.FromResult(Results.BadRequest("Query parameters 'from' and 'to' are required."));

    private async Task<IResult> HandleAsync(string from, string to, INotificationOperatorService service)
    {
        if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromValue) ||
            !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toValue))
        {
            return Results.BadRequest("Query parameters 'from' and 'to' must be ISO-8601 date-times.");
        }

        var report = await service.ReconcileAsync(fromValue, toValue);
        return Results.Ok(new ReconciliationReportResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            EshopOnly = report.EshopOnly.Select(ToDto).ToList()
        });
    }

    private static ReconciliationEntryDto ToDto(ReconciliationEntry entry) => new()
    {
        NotificationId = entry.NotificationId,
        ProviderMessageSid = entry.ProviderMessageSid,
        Source = entry.Source,
        Status = entry.Status,
        DateSent = entry.DateSent,
        BodyPreview = entry.BodyPreview
    };
}
