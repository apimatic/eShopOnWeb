using System;
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

public class ReconciliationReportResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public ReconciliationMatchDto[] Matched { get; set; } = Array.Empty<ReconciliationMatchDto>();
    public ReconciliationProviderOnlyDto[] ProviderOnly { get; set; } = Array.Empty<ReconciliationProviderOnlyDto>();
    public ReconciliationEshopOnlyDto[] EshopOnly { get; set; } = Array.Empty<ReconciliationEshopOnlyDto>();
}

public class ReconciliationMatchDto
{
    public int NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string EshopStatus { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
}

public class ReconciliationProviderOnlyDto
{
    public string? ProviderSid { get; set; }
    public string? ProviderStatus { get; set; }
}

public class ReconciliationEshopOnlyDto
{
    public int NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string EshopStatus { get; set; } = string.Empty;
}

public class GetNotificationReconciliationEndpoint : IEndpoint<IResult, HttpContext, IOperatorOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOperatorOrderService operatorOrderService) =>
            {
                if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromValue)
                    || !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toValue))
                {
                    return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
                }

                var report = await operatorOrderService.ReconcileAsync(fromValue, toValue);
                return Results.Ok(new ReconciliationReportResponse
                {
                    From = report.From,
                    To = report.To,
                    FromNumber = report.FromNumber,
                    Matched = report.Matched.Select(m => new ReconciliationMatchDto
                    {
                        NotificationId = m.Notification.Id,
                        ProviderSid = m.ProviderMessage.Sid,
                        EshopStatus = m.Notification.ProviderStatus,
                        ProviderStatus = m.ProviderMessage.Status
                    }).ToArray(),
                    ProviderOnly = report.ProviderOnly.Select(p => new ReconciliationProviderOnlyDto
                    {
                        ProviderSid = p.Sid,
                        ProviderStatus = p.Status
                    }).ToArray(),
                    EshopOnly = report.EshopOnly.Select(e => new ReconciliationEshopOnlyDto
                    {
                        NotificationId = e.Id,
                        ProviderSid = e.ProviderMessageSid,
                        EshopStatus = e.ProviderStatus
                    }).ToArray()
                });
            })
            .Produces<ReconciliationReportResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(HttpContext httpContext, IOperatorOrderService operatorOrderService)
        => Task.FromResult(Results.BadRequest());
}
