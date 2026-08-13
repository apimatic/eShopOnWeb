using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.PublicApi.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationRequest
{
    /// <summary>ISO-8601 start of the range (inclusive).</summary>
    [FromQuery(Name = "from")] public DateTimeOffset From { get; set; }

    /// <summary>ISO-8601 end of the range (inclusive).</summary>
    [FromQuery(Name = "to")] public DateTimeOffset To { get; set; }
}

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; init; }
    public string? ProviderStatus { get; init; }
    public string? MaskedTo { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public int? NotificationId { get; init; }
    public int? OrderId { get; init; }
    public string? Kind { get; init; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderMessageCount { get; set; }
    public int EShopMessageCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
}

/// <summary>
/// Operator action: lists the provider's own record of messages for a date range and lines it up against
/// what eShop believes it sent, so a message one side knows about and the other doesn't is visible. Counts
/// only messages sent from this application's own configured sending number.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReconciliationEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([AsParameters] ReconciliationRequest request, IOrderNotificationService service) =>
                await HandleAsync(request, service))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest(new { errors = new[] { "'to' must not be earlier than 'from'." } });
        }

        var report = await service.ReconcileAsync(request.From, request.To, _httpContextAccessor.RequestAborted());

        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            ProviderMessageCount = report.ProviderMessageCount,
            EShopMessageCount = report.EShopMessageCount,
            MatchedCount = report.MatchedCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            EShopOnly = report.EShopOnly.Select(ToDto).ToList()
        });
    }

    private static ReconciliationEntryDto ToDto(ReconciliationEntry entry) => new()
    {
        ProviderMessageSid = entry.ProviderMessageSid,
        ProviderStatus = entry.ProviderStatus,
        MaskedTo = entry.MaskedTo,
        DateSent = entry.DateSent,
        NotificationId = entry.NotificationId,
        OrderId = entry.OrderId,
        Kind = entry.Kind?.ToString()
    };
}
