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

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
}

public class ReconciliationRowDto
{
    public string? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string Match { get; set; } = string.Empty;
    public string? ApplicationStatus { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public string? Kind { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int ApplicationOnlyCount { get; set; }
    public List<ReconciliationRowDto> Matched { get; set; } = new();
    public List<ReconciliationRowDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationRowDto> ApplicationOnly { get; set; } = new();
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, notifications);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService notifications)
    {
        var report = await notifications.ReconcileAsync(request.From, request.To);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            MatchedCount = report.Matched.Count,
            ProviderOnlyCount = report.ProviderOnly.Count,
            ApplicationOnlyCount = report.ApplicationOnly.Count,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            ApplicationOnly = report.ApplicationOnly.Select(ToDto).ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciliationRowDto ToDto(ReconciliationRow row) => new()
    {
        NotificationId = row.NotificationId,
        ProviderMessageSid = row.ProviderMessageSid,
        Match = row.Match,
        ApplicationStatus = row.ApplicationStatus,
        ProviderStatus = row.ProviderStatus,
        ProviderDateSent = row.ProviderDateSent,
        Kind = row.Kind?.ToString()
    };
}
