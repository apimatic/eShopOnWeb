using System;
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
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, HttpContext http, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), http, notifications);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService notifications)
        => HandleAsync(request, null!, notifications);

    private async Task<IResult> HandleAsync(
        ReconciliationRequest request,
        HttpContext http,
        IOrderNotificationService notifications)
    {
        if (request.To <= request.From)
        {
            return Results.BadRequest(new { message = "'to' must be later than 'from'." });
        }

        var report = await notifications.ReconcileAsync(request.From, request.To, http.RequestAborted);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Truncated = report.Truncated,
            MatchedCount = report.Matched.Count,
            ProviderOnlyCount = report.ProviderOnly.Count,
            ApplicationOnlyCount = report.ApplicationOnly.Count,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            ApplicationOnly = report.ApplicationOnly.Select(ToDto).ToList()
        });
    }

    private static ReconciliationItemDto ToDto(ReconciledNotification item)
        => new()
        {
            NotificationId = item.NotificationId,
            ProviderSid = item.ProviderSid,
            Status = item.Status,
            Body = item.Body,
            DateSent = item.DateSent,
            ErrorCode = item.ErrorCode,
            Source = item.Source
        };
}
