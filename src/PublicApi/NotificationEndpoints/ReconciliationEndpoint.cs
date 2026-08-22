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

    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationRowDto
{
    public string? ProviderSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? DateSent { get; set; }
    public int? NotificationId { get; set; }
    public string Alignment { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderCount { get; set; }
    public int EshopCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
    public bool Truncated { get; set; }
    public List<ReconciliationRowDto> Messages { get; set; } = new();
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, HttpContext httpContext, IShopperOrderService service) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), httpContext, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IShopperOrderService service)
        => HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(ReconciliationRequest request, HttpContext httpContext, IShopperOrderService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To, httpContext.RequestAborted);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            ProviderCount = report.ProviderCount,
            EshopCount = report.EshopCount,
            MatchedCount = report.MatchedCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            EshopOnlyCount = report.EshopOnlyCount,
            Truncated = report.Truncated,
            Messages = report.Messages.Select(m => new ReconciliationRowDto
            {
                ProviderSid = m.ProviderSid,
                ProviderStatus = m.ProviderStatus,
                DateSent = m.DateSent,
                NotificationId = m.NotificationId,
                Alignment = m.Alignment
            }).ToList()
        });
    }
}
