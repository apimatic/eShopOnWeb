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

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset? from, DateTimeOffset? to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest { From = from, To = to }, service);
            })
            .Produces<ReconcileNotificationsResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconcileNotificationsRequest request, IOrderNotificationService service)
    {
        return EndpointHelpers.ExecuteAsync(async () =>
        {
            if (!request.From.HasValue || !request.To.HasValue)
            {
                return Results.BadRequest(new { message = "Both 'from' and 'to' query parameters are required as ISO-8601 date-times." });
            }

            var report = await service.ReconcileAsync(request.From.Value, request.To.Value);
            return Results.Ok(new ReconcileNotificationsResponse
            {
                From = report.From,
                To = report.To,
                Matched = report.Matched.Select(ReconciliationEntryDto.From).ToList(),
                ProviderOnly = report.ProviderOnly.Select(ReconciliationEntryDto.From).ToList(),
                LocalOnly = report.LocalOnly.Select(ReconciliationEntryDto.From).ToList(),
                ProviderCount = report.Matched.Count + report.ProviderOnly.Count,
                LocalCount = report.Matched.Count + report.LocalOnly.Count
            });
        });
    }
}

public class ReconcileNotificationsRequest : BaseRequest
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

public class ReconcileNotificationsResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderCount { get; set; }
    public int LocalCount { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> LocalOnly { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? LocalStatus { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? DateCreated { get; set; }

    public static ReconciliationEntryDto From(ReconciliationEntry entry)
    {
        return new ReconciliationEntryDto
        {
            ProviderMessageSid = entry.ProviderMessageSid,
            NotificationId = entry.NotificationId,
            LocalStatus = entry.LocalStatus,
            ProviderStatus = entry.ProviderStatus,
            DateCreated = entry.DateCreated
        };
    }
}
