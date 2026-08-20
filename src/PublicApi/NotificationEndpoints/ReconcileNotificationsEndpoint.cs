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

public class ReconcileNotificationsRequest : BaseRequest
{
    public string? From { get; init; }
    public string? To { get; init; }
}

public class ReconciliationRowDto
{
    public string? ProviderSid { get; set; }
    public string Alignment { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? ApplicationStatus { get; set; }
    public int? NotificationId { get; set; }
    public string? ProviderDateSent { get; set; }
    public string? Body { get; set; }
}

public class ReconciliationReportResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public bool Truncated { get; set; }
    public List<ReconciliationRowDto> Rows { get; set; } = new();
}

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, HttpContext httpContext, INotificationOperatorService operatorService) =>
            {
                if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromValue)
                    || !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toValue))
                {
                    return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
                }

                try
                {
                    var report = await operatorService.ReconcileAsync(fromValue, toValue, httpContext.RequestAborted);
                    return Results.Ok(new ReconciliationReportResponse
                    {
                        From = report.From,
                        To = report.To,
                        Truncated = report.Truncated,
                        Rows = report.Rows.Select(r => new ReconciliationRowDto
                        {
                            ProviderSid = r.ProviderSid,
                            Alignment = r.Alignment,
                            ProviderStatus = r.ProviderStatus,
                            ApplicationStatus = r.ApplicationStatus,
                            NotificationId = r.NotificationId,
                            ProviderDateSent = r.ProviderDateSent,
                            Body = r.ProviderBodyPresent
                        }).ToList()
                    });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
                catch (SmsProviderException)
                {
                    return Results.Json(new { message = "The provider could not produce a reconciliation listing." }, statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Produces<ReconciliationReportResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconcileNotificationsRequest request, INotificationOperatorService operatorService)
        => Task.FromResult(Results.Ok());
}
