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

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsEndpoint.Request, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, HttpContext http) =>
                await HandleAsync(new Request { From = from, To = to }, http))
            .Produces<Response>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, HttpContext http)
    {
        try
        {
            var report = await http.GetRequired<IOrderNotificationService>()
                .ReconcileAsync(request.From, request.To);
            return Results.Ok(new Response
            {
                From = report.From,
                To = report.To,
                FromNumber = report.FromNumber,
                Matched = report.Matched.Select(m => new MatchedItem
                {
                    NotificationId = m.Notification.Id,
                    ProviderMessageSid = m.ProviderMessage.Sid,
                    ApplicationStatus = m.Notification.ProviderStatus,
                    ProviderStatus = m.ProviderMessage.Status
                }).ToList(),
                ProviderOnly = report.ProviderOnly.Select(m => new ProviderItem
                {
                    ProviderMessageSid = m.Sid,
                    ProviderStatus = m.Status,
                    DateSent = m.DateSent,
                    DateCreated = m.DateCreated
                }).ToList(),
                ApplicationOnly = report.ApplicationOnly.Select(n => new ApplicationItem
                {
                    NotificationId = n.Id,
                    ProviderMessageSid = n.ProviderMessageSid,
                    ProviderStatus = n.ProviderStatus,
                    Type = n.Type.ToString(),
                    CreatedAt = n.CreatedAt
                }).ToList()
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    public class Request
    {
        public DateTimeOffset From { get; set; }
        public DateTimeOffset To { get; set; }
    }

    public class Response
    {
        public DateTimeOffset From { get; set; }
        public DateTimeOffset To { get; set; }
        public string FromNumber { get; set; } = string.Empty;
        public List<MatchedItem> Matched { get; set; } = new();
        public List<ProviderItem> ProviderOnly { get; set; } = new();
        public List<ApplicationItem> ApplicationOnly { get; set; } = new();
    }

    public class MatchedItem
    {
        public int NotificationId { get; set; }
        public string? ProviderMessageSid { get; set; }
        public string ApplicationStatus { get; set; } = string.Empty;
        public string ProviderStatus { get; set; } = string.Empty;
    }

    public class ProviderItem
    {
        public string? ProviderMessageSid { get; set; }
        public string ProviderStatus { get; set; } = string.Empty;
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
    }

    public class ApplicationItem
    {
        public int NotificationId { get; set; }
        public string? ProviderMessageSid { get; set; }
        public string ProviderStatus { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
    }
}
