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

public class ReconciliationEndpoint : IEndpoint<IResult, INotificationAdminService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, INotificationAdminService service) =>
            {
                return await HandleAsync(service, from, to);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(INotificationAdminService service)
        => throw new NotSupportedException("from and to query parameters are required.");

    private async Task<IResult> HandleAsync(INotificationAdminService service, DateTimeOffset from, DateTimeOffset to)
    {
        try
        {
            var report = await service.ReconcileAsync(from, to);
            return Results.Ok(new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                Matched = report.Matched.Select(m => new ReconciliationMatchDto
                {
                    Notification = NotificationDto.From(m.Notification),
                    ProviderMessageSid = m.ProviderMessage.Sid,
                    ProviderStatus = m.ProviderMessage.Status,
                    ProviderDateSent = m.ProviderMessage.DateSent,
                    ProviderDateCreated = m.ProviderMessage.DateCreated
                }).ToList(),
                ProviderOnly = report.ProviderOnly.Select(m => new ProviderMessageDto
                {
                    Sid = m.Sid,
                    Status = m.Status,
                    DateSent = m.DateSent,
                    DateCreated = m.DateCreated,
                    Direction = m.Direction
                }).ToList(),
                ApplicationOnly = report.ApplicationOnly.Select(NotificationDto.From).ToList()
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<ProviderMessageDto> ProviderOnly { get; set; } = new();
    public List<NotificationDto> ApplicationOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public NotificationDto Notification { get; set; } = new();
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ProviderDateSent { get; set; }
    public string? ProviderDateCreated { get; set; }
}

public class ProviderMessageDto
{
    public string? Sid { get; set; }
    public string? Status { get; set; }
    public string? DateSent { get; set; }
    public string? DateCreated { get; set; }
    public string? Direction { get; set; }
}
