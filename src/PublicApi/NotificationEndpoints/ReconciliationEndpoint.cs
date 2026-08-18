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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report: lists the provider's own record of messages for a date range (from this app's
/// configured sending number only) and lines them up against what eShop believes it sent.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
    {
        if (!TryParseIso(request.From, out var from))
            return Results.BadRequest(new { error = "'from' must be an ISO-8601 date-time." });
        if (!TryParseIso(request.To, out var to))
            return Results.BadRequest(new { error = "'to' must be an ISO-8601 date-time." });
        if (from > to)
            return Results.BadRequest(new { error = "'from' must not be later than 'to'." });

        try
        {
            var report = await service.ReconcileAsync(from, to);
            return Results.Ok(ReconciliationResponse.Create(request.CorrelationId(), report));
        }
        catch (SmsNotificationException ex)
        {
            return ProviderErrorResults.From(ex);
        }
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result))
        {
            return true;
        }

        result = default;
        return false;
    }
}

public class ReconciliationRequest : BaseRequest
{
    public string? From { get; set; }
    public string? To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconciliationResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderMessageCount { get; set; }
    public int EShopMessageCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();

    public static ReconciliationResponse Create(Guid correlationId, ReconciliationReport report) => new(correlationId)
    {
        From = report.From,
        To = report.To,
        FromNumber = report.FromNumber,
        ProviderMessageCount = report.ProviderMessageCount,
        EShopMessageCount = report.EShopMessageCount,
        MatchedCount = report.MatchedCount,
        Matched = report.Matched.Select(ReconciliationEntryDto.From).ToList(),
        ProviderOnly = report.ProviderOnly.Select(ReconciliationEntryDto.From).ToList(),
        EShopOnly = report.EShopOnly.Select(ReconciliationEntryDto.From).ToList()
    };
}

public class ReconciliationEntryDto
{
    public string? Sid { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }

    public static ReconciliationEntryDto From(ReconciliationEntry entry) => new()
    {
        Sid = entry.Sid,
        Status = entry.Status,
        DateSent = entry.DateSent,
        NotificationId = entry.NotificationId,
        OrderId = entry.OrderId
    };
}
