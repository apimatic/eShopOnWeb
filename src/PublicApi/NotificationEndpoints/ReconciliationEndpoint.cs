using System;
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

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly IOrderNotificationService _notifications;

    public ReconciliationEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, HttpContext httpContext) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromValue) || !DateTimeOffset.TryParse(to, out var toValue))
                {
                    return Results.BadRequest(new { message = "'from' and 'to' must be ISO-8601 date-times." });
                }

                return await HandleAsync(new ReconciliationRequest
                {
                    From = fromValue,
                    To = toValue,
                    CancellationToken = httpContext.RequestAborted
                });
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest(new { message = "'to' must be greater than or equal to 'from'." });
        }

        try
        {
            var report = await _notifications.ReconcileAsync(request.From, request.To, request.CancellationToken);
            return Results.Ok(new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                Truncated = report.Truncated,
                Matched = report.Matched.Select(Map).ToList(),
                OnlyInProvider = report.OnlyInProvider.Select(Map).ToList(),
                OnlyInApplication = report.OnlyInApplication.Select(Map).ToList()
            });
        }
        catch (OrderMessagingException)
        {
            return Results.Json(new { message = "The messaging provider is unavailable." }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static ReconciliationItemDto Map(ReconciliationItem item)
    {
        return new ReconciliationItemDto
        {
            ProviderSid = item.ProviderSid,
            NotificationId = item.NotificationId,
            Status = item.Status,
            Body = item.Body,
            DateSent = item.DateSent,
            Source = item.Source
        };
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    internal CancellationToken CancellationToken { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public bool Truncated { get; set; }
    public System.Collections.Generic.List<ReconciliationItemDto> Matched { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationItemDto> OnlyInProvider { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationItemDto> OnlyInApplication { get; set; } = new();
}

public class ReconciliationItemDto
{
    public string? ProviderSid { get; set; }
    public int? NotificationId { get; set; }
    public string? Status { get; set; }
    public string? Body { get; set; }
    public string? DateSent { get; set; }
    public string Source { get; set; } = string.Empty;
}
