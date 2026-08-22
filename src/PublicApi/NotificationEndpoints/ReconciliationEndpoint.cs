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

public class ReconciliationMessageDto
{
    public string? NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
    public string? Body { get; set; }
    public string? DateCreated { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMessageDto> Matched { get; set; } = new();
    public List<ReconciliationMessageDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationMessageDto> EshopOnly { get; set; } = new();
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOperatorOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReconciliationEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOperatorOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOperatorOrderNotificationService service)
    {
        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? default;
        var report = await service.ReconcileAsync(request.From, request.To, ct);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            EshopOnly = report.EshopOnly.Select(ToDto).ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciliationMessageDto ToDto(ReconciliationMessage message) => new()
    {
        NotificationId = message.NotificationId,
        ProviderSid = message.ProviderSid,
        Status = message.Status,
        Body = message.Body,
        DateCreated = message.DateCreated,
        Source = message.Source
    };
}
