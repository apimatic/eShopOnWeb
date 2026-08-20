using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EshopStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> EshopOnly { get; set; } = new();
}

public class ReconciliationEndpoint : IEndpoint<IResult, EmptyRequest, IShopOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async ([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, IShopOrderService orderService) =>
            {
                return await HandleAsync(new EmptyRequest(), orderService, from, to);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, IShopOrderService orderService)
        => HandleAsync(request, orderService, default, default);

    private async Task<IResult> HandleAsync(EmptyRequest request, IShopOrderService orderService, DateTimeOffset from, DateTimeOffset to)
    {
        var report = await orderService.ReconcileAsync(from, to);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            EshopOnly = report.EshopOnly.Select(ToDto).ToList()
        });
    }

    private static ReconciliationEntryDto ToDto(ReconciliationEntry entry) => new()
    {
        ProviderMessageSid = entry.ProviderMessageSid,
        NotificationId = entry.NotificationId,
        ProviderStatus = entry.ProviderStatus,
        EshopStatus = entry.EshopStatus,
        DateSent = entry.DateSent
    };
}
