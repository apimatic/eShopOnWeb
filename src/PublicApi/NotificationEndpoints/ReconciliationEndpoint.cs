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
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from) || !DateTimeOffset.TryParse(request.To, out var to))
        {
            return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
        }

        var report = await service.ReconcileAsync(from, to);
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

    private static ReconciledMessageDto ToDto(ReconciledMessage message)
        => new()
        {
            NotificationId = message.NotificationId,
            ProviderSid = message.ProviderSid,
            ProviderStatus = message.ProviderStatus,
            EshopStatus = message.EshopStatus,
            Kind = message.Kind,
            Body = message.Body,
            CreatedAt = message.CreatedAt
        };
}

public class ReconciliationRequest : BaseRequest
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciledMessageDto> Matched { get; set; } = [];
    public List<ReconciledMessageDto> ProviderOnly { get; set; } = [];
    public List<ReconciledMessageDto> EshopOnly { get; set; } = [];
}

public class ReconciledMessageDto
{
    public int? NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EshopStatus { get; set; }
    public string? Kind { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
