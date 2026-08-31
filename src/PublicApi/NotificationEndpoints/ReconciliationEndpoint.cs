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
using Microsoft.eShopWeb.ApplicationCore.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator: lines up the provider's own record of messages for a date range against what
/// eShop believes it sent. Only traffic from this application's configured sending number
/// is asked for. from/to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
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
            (string? from, string? to) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to));
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        if (!DateTimeOffset.TryParse(request.From, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var from) ||
            !DateTimeOffset.TryParse(request.To, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var to))
        {
            return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
        }
        if (to < from)
        {
            return Results.BadRequest(new { message = "to must not be earlier than from." });
        }

        try
        {
            var report = await _orderNotificationService.ReconcileAsync(from, to);
            var response = new ReconciliationResponse(request.CorrelationId())
            {
                From = from,
                To = to,
                Matched = report.Matched.Select(ToDto).ToList(),
                ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
                LocalOnly = report.LocalOnly.Select(ToDto).ToList()
            };
            return Results.Ok(response);
        }
        catch (SmsProviderException)
        {
            return Results.Problem("The messaging provider could not be reached; the report could not be built.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static ReconciliationEntryDto ToDto(ReconciliationEntry entry) => new()
    {
        NotificationId = entry.NotificationId,
        MessageSid = entry.MessageSid,
        To = entry.To,
        Status = entry.Status,
        Date = entry.Date
    };
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(string? from, string? to)
    {
        From = from;
        To = to;
    }

    public string? From { get; }
    public string? To { get; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> LocalOnly { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public int? NotificationId { get; set; }
    public string? MessageSid { get; set; }
    public string? To { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? Date { get; set; }
}
