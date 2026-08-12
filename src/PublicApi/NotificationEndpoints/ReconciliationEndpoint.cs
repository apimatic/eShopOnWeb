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
using Microsoft.eShopWeb.ApplicationCore.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }

    public static ReconciliationEntryDto From(ReconciliationEntry e) => new()
    {
        ProviderMessageSid = e.ProviderMessageSid,
        NotificationId = e.NotificationId,
        OrderId = e.OrderId,
        ProviderStatus = e.ProviderStatus,
        EShopStatus = e.EShopStatus,
        DateSent = e.DateSent
    };
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }

    /// <summary>Messages both the provider and eShop agree on.</summary>
    public List<ReconciliationEntryDto> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider's range query did not return.</summary>
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
}

/// <summary>
/// Operator action: reconciles the provider's own record of messages for this application's configured
/// sending number over a date range against what eShop believes it sent. <c>from</c>/<c>to</c> are ISO-8601.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IOrderNotificationService service) =>
            {
                if (!TryParseIso(from, out var fromDto) || !TryParseIso(to, out var toDto))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["range"] = new[] { "'from' and 'to' are required and must be ISO-8601 date-times." }
                    });
                }
                if (toDto < fromDto)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["range"] = new[] { "'to' must not be earlier than 'from'." }
                    });
                }
                return await HandleAsync(new ReconciliationRequest { From = fromDto, To = toDto }, service);
            })
            .Produces<ReconciliationResponse>()
            .ProducesValidationProblem()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            ProviderCount = report.ProviderCount,
            EShopCount = report.EShopCount,
            Matched = report.Matched.Select(ReconciliationEntryDto.From).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ReconciliationEntryDto.From).ToList(),
            EShopOnly = report.EShopOnly.Select(ReconciliationEntryDto.From).ToList()
        };
        return Results.Ok(response);
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result))
        {
            return true;
        }
        result = default;
        return false;
    }
}
