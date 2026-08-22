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

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
    {
        if (request.From is null || request.To is null)
        {
            return Results.BadRequest(new { error = "Query parameters 'from' and 'to' are required ISO-8601 date-times." });
        }

        var report = await service.ReconcileAsync(request.From.Value, request.To.Value);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Items = report.Items.Select(i => new ReconciliationItemDto
            {
                ProviderMessageSid = i.ProviderMessageSid,
                NotificationId = i.NotificationId,
                Match = i.Match,
                ProviderStatus = i.ProviderStatus,
                ApplicationStatus = i.ApplicationStatus,
                ProviderDateSent = i.ProviderDateSent,
                ApplicationCreatedAt = i.ApplicationCreatedAt
            }).ToList()
        });
    }
}

public class ReconciliationRequest
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciliationItemDto> Items { get; set; } = new();
}

public class ReconciliationItemDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string Match { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? ApplicationStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public DateTimeOffset? ApplicationCreatedAt { get; set; }
}
