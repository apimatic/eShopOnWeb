using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: a report lining up the provider's own record of messages sent from this
/// application's configured sending number, over an ISO-8601 date-time range, against what eShop
/// believes it sent — so a message one side knows about and the other does not is visible. The
/// provider is asked to filter by that number and range, not filtered after the fact. Admin only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, HttpContext http, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service, http.RequestAborted);
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        if (request.From > request.To)
        {
            return Results.Problem(detail: "'from' must not be after 'to'.", statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid date range.");
        }

        try
        {
            var report = await service.ReconcileAsync(request.From, request.To, ct);
            var response = new ReconciliationResponse(request.CorrelationId())
            {
                From = report.From,
                To = report.To,
                SendingNumber = report.SendingNumber,
                MatchedCount = report.Matched.Count,
                KnownToProviderOnlyCount = report.KnownToProviderOnly.Count,
                KnownToEShopOnlyCount = report.KnownToEShopOnly.Count,
                Matched = report.Matched.Select(m => new ReconciliationMatchDto
                {
                    ProviderMessageSid = m.ProviderMessageSid,
                    ProviderStatus = m.ProviderStatus,
                    NotificationId = m.NotificationId,
                    Kind = m.Kind.ToString(),
                    EShopStatus = m.EShopStatus
                }).ToList(),
                KnownToProviderOnly = report.KnownToProviderOnly.Select(p => new ReconciliationProviderOnlyDto
                {
                    ProviderMessageSid = p.ProviderMessageSid,
                    ProviderStatus = p.ProviderStatus,
                    DateSent = p.DateSent
                }).ToList(),
                KnownToEShopOnly = report.KnownToEShopOnly.Select(e => new ReconciliationEShopOnlyDto
                {
                    NotificationId = e.NotificationId,
                    ProviderMessageSid = e.ProviderMessageSid,
                    Kind = e.Kind.ToString(),
                    EShopStatus = e.EShopStatus
                }).ToList()
            };
            return Results.Ok(response);
        }
        catch (SmsGatewayException)
        {
            return Results.Problem(detail: "The provider could not be reached to build the reconciliation report. Please try again.",
                statusCode: StatusCodes.Status502BadGateway, title: "Messaging provider unavailable.");
        }
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string SendingNumber { get; set; } = string.Empty;

    public int MatchedCount { get; set; }
    public int KnownToProviderOnlyCount { get; set; }
    public int KnownToEShopOnlyCount { get; set; }

    /// <summary>Messages both the provider and eShop agree on.</summary>
    public List<ReconciliationMatchDto> Matched { get; set; } = new();

    /// <summary>Messages the provider records from this number that eShop has no record of.</summary>
    public List<ReconciliationProviderOnlyDto> KnownToProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent from this number that the provider did not return.</summary>
    public List<ReconciliationEShopOnlyDto> KnownToEShopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string EShopStatus { get; set; } = string.Empty;
}

public class ReconciliationProviderOnlyDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconciliationEShopOnlyDto
{
    public int NotificationId { get; set; }
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string EShopStatus { get; set; } = string.Empty;
}
