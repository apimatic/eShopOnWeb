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

public class GetNotificationReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class GetNotificationReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciledMessageDto> Matched { get; set; } = new();
    public List<ProviderOnlyMessageDto> ProviderOnly { get; set; } = new();
    public List<ApplicationOnlyMessageDto> ApplicationOnly { get; set; } = new();
}

public class ReconciledMessageDto
{
    public int NotificationId { get; set; }
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string ApplicationStatus { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
}

public class ProviderOnlyMessageDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? DateCreated { get; set; }
}

public class ApplicationOnlyMessageDto
{
    public int NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class GetNotificationReconciliationEndpoint : IEndpoint<IResult, GetNotificationReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new GetNotificationReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<GetNotificationReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetNotificationReconciliationRequest request, IOrderNotificationService service)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest(new { message = "The 'to' timestamp must be on or after 'from'." });
        }

        var report = await service.ReconcileAsync(request.From, request.To);
        return Results.Ok(new GetNotificationReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Matched = report.Matched.Select(m => new ReconciledMessageDto
            {
                NotificationId = m.NotificationId,
                ProviderMessageSid = m.ProviderMessageSid,
                ApplicationStatus = m.ApplicationStatus,
                ProviderStatus = m.ProviderStatus
            }).ToList(),
            ProviderOnly = report.ProviderOnly.Select(m => new ProviderOnlyMessageDto
            {
                ProviderMessageSid = m.ProviderMessageSid,
                Status = m.Status,
                DateCreated = m.DateCreated
            }).ToList(),
            ApplicationOnly = report.ApplicationOnly.Select(m => new ApplicationOnlyMessageDto
            {
                NotificationId = m.NotificationId,
                ProviderMessageSid = m.ProviderMessageSid,
                Status = m.Status
            }).ToList()
        });
    }
}
