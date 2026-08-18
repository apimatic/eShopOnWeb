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
    public string? Sid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? NotificationId { get; set; }
    public string? Kind { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int MatchedCount { get; set; }

    /// <summary>Messages both the provider and eShop know about.</summary>
    public List<ReconciliationEntryDto> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about that eShop does not.</summary>
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider's record does not show.</summary>
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
}

/// <summary>
/// Operator action: lists the provider's own record of messages from the configured sending
/// number over a date range and lines them up against what eShop believes it sent, so a message
/// one side knows about and the other does not is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset?, DateTimeOffset?>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public ReconciliationEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to) => await HandleAsync(from, to))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is null || to is null)
        {
            return Results.BadRequest(new { error = "Both 'from' and 'to' ISO-8601 date-times are required." });
        }
        if (from > to)
        {
            return Results.BadRequest(new { error = "'from' must not be after 'to'." });
        }

        var report = await _orderNotificationService.ReconcileAsync(from.Value, to.Value);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            ProviderCount = report.ProviderCount,
            EShopCount = report.EShopCount,
            MatchedCount = report.MatchedCount,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            EShopOnly = report.EShopOnly.Select(ToDto).ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciliationEntryDto ToDto(ReconciliationEntry entry) => new()
    {
        Sid = entry.Sid,
        Status = entry.Status,
        ErrorCode = entry.ErrorCode,
        DateSent = entry.DateSent,
        NotificationId = entry.NotificationId,
        Kind = entry.Kind?.ToString()
    };
}
