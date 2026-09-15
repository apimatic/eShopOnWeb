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
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report: the provider's own record of messages sent from this application's configured
/// sending number over a date range, lined up against what eShop believes it sent. A message the
/// provider knows about and eShop doesn't — or the reverse — is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService orderNotificationService) =>
            {
                return await HandleAsync(from, to, orderNotificationService);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderNotificationService orderNotificationService)
    {
        if (to < from)
        {
            return Results.BadRequest(new { error = "'to' must not be earlier than 'from'." });
        }

        var report = await orderNotificationService.ReconcileAsync(from, to);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            MatchedCount = report.Matched.Count,
            ProviderOnlyCount = report.ProviderOnly.Count,
            EShopOnlyCount = report.EShopOnly.Count,
            Matched = report.Matched
                .Select(m => new ReconciliationMatchDto
                {
                    Local = NotificationDto.From(m.Local),
                    Provider = ProviderMessageDto.From(m.Provider)
                })
                .ToList(),
            ProviderOnly = report.ProviderOnly.Select(ProviderMessageDto.From).ToList(),
            EShopOnly = report.EShopOnly.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;

    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }

    /// <summary>Messages both the provider and eShop have a record of.</summary>
    public List<ReconciliationMatchDto> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public List<ProviderMessageDto> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider's ranged record does not show.</summary>
    public List<NotificationDto> EShopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public NotificationDto Local { get; set; } = new();
    public ProviderMessageDto Provider { get; set; } = new();
}

/// <summary>The provider's view of a message. The destination number is deliberately not exposed.</summary>
public class ProviderMessageDto
{
    public string Sid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? FromNumber { get; set; }
    public DateTimeOffset? DateSent { get; set; }

    public static ProviderMessageDto From(ProviderMessage message) => new()
    {
        Sid = message.Sid,
        Status = message.Status,
        ErrorCode = message.ErrorCode,
        FromNumber = message.From,
        DateSent = message.DateSent
    };
}
