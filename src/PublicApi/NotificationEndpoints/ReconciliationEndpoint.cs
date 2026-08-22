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

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOperatorNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOperatorNotificationService operatorNotificationService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), operatorNotificationService);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOperatorNotificationService operatorNotificationService)
    {
        var report = await operatorNotificationService.ReconcileAsync(request.From, request.To);
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            MatchedCount = report.MatchedCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            LocalOnlyCount = report.LocalOnlyCount,
            Messages = report.Messages.Select(m => new ReconciliationMessageDto
            {
                ProviderMessageSid = m.ProviderMessageSid,
                Status = m.Status,
                Body = m.Body,
                From = m.From,
                To = m.To,
                DateCreated = m.DateCreated,
                DateSent = m.DateSent,
                NotificationId = m.LocalNotificationId,
                OrderId = m.LocalOrderId,
                Alignment = m.Alignment
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int LocalOnlyCount { get; set; }
    public List<ReconciliationMessageDto> Messages { get; set; } = new();
}

public class ReconciliationMessageDto
{
    public string? ProviderMessageSid { get; set; }
    public string? Status { get; set; }
    public string? Body { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? DateCreated { get; set; }
    public string? DateSent { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string Alignment { get; set; } = string.Empty;
}
