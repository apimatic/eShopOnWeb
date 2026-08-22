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
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationMessageDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ApplicationStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMessageDto> Matched { get; set; } = new();
    public List<ReconciliationMessageDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationMessageDto> ApplicationOnly { get; set; } = new();
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, INotificationOperatorService notifications) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, notifications);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, INotificationOperatorService notifications)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest(new { message = "The 'to' timestamp must be on or after 'from'." });
        }

        var report = await notifications.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            ApplicationOnly = report.ApplicationOnly.Select(ToDto).ToList()
        });
    }

    private static ReconciliationMessageDto ToDto(ReconciliationMessage message) => new()
    {
        ProviderMessageSid = message.ProviderMessageSid,
        NotificationId = message.NotificationId,
        ProviderStatus = message.ProviderStatus,
        ApplicationStatus = message.ApplicationStatus,
        DateSent = message.DateSent
    };
}
