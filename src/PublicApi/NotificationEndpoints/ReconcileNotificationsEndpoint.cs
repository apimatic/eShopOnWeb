using System;
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

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconciliationQueryRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOrderNotificationService orderService) =>
            {
                return await HandleAsync(new ReconciliationQueryRequest(from, to), orderService);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQueryRequest request, IOrderNotificationService orderService)
    {
        if (!DateTimeOffset.TryParse(request.From, out var fromUtc) || !DateTimeOffset.TryParse(request.To, out var toUtc))
        {
            return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
        }

        var report = await orderService.ReconcileAsync(fromUtc, toUtc);
        return Results.Ok(new ReconciliationResponse
        {
            FromNumber = report.FromNumber,
            From = report.From,
            To = report.To,
            MatchedCount = report.Matched.Count,
            ProviderOnlyCount = report.ProviderOnly.Count,
            ApplicationOnlyCount = report.ApplicationOnly.Count,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            ApplicationOnly = report.ApplicationOnly.Select(ToDto).ToList()
        });
    }

    private static ReconciliationItemDto ToDto(ReconciledMessage message) =>
        new()
        {
            ProviderMessageSid = message.ProviderMessageSid,
            NotificationId = message.NotificationId,
            ProviderStatus = message.ProviderStatus,
            ApplicationStatus = message.ApplicationStatus
        };
}

public class ReconciliationQueryRequest : BaseRequest
{
    public ReconciliationQueryRequest(string from, string to)
    {
        From = from;
        To = to;
    }

    public string From { get; }
    public string To { get; }
}

public class ReconciliationResponse
{
    public string FromNumber { get; set; } = string.Empty;
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int ApplicationOnlyCount { get; set; }
    public System.Collections.Generic.List<ReconciliationItemDto> Matched { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationItemDto> ProviderOnly { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationItemDto> ApplicationOnly { get; set; } = new();
}

public class ReconciliationItemDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ApplicationStatus { get; set; }
}
