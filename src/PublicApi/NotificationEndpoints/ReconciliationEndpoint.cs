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

public class ReconciliationQuery
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
}

public class ReconciliationEntryDto
{
    public int? NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
    public string? Kind { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public bool Truncated { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> EshopOnly { get; set; } = new();
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, INotificationAdminService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReconciliationEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, INotificationAdminService service) =>
            {
                return await HandleAsync(new ReconciliationQuery { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, INotificationAdminService service)
    {
        try
        {
            var report = await service.ReconcileAsync(request.From, request.To, _httpContextAccessor.HttpContext!.RequestAborted);
            return Results.Ok(new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                Truncated = report.Truncated,
                Matched = report.Matched.Select(Map).ToList(),
                ProviderOnly = report.ProviderOnly.Select(Map).ToList(),
                EshopOnly = report.EshopOnly.Select(Map).ToList()
            });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static ReconciliationEntryDto Map(ReconciliationRow row)
    {
        int? id = null;
        if (int.TryParse(row.NotificationId, out var parsed))
        {
            id = parsed;
        }

        return new ReconciliationEntryDto
        {
            NotificationId = id,
            ProviderSid = row.ProviderSid,
            Status = row.Status,
            Kind = row.Kind
        };
    }
}
