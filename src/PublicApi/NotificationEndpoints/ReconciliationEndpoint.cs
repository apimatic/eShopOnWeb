using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, IShopperOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IShopperOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromUtc) || !DateTimeOffset.TryParse(to, out var toUtc))
                    throw new InvalidOrderOperationException("from and to must be ISO-8601 date-times.");

                var report = await service.ReconcileAsync(fromUtc, toUtc, cancellationToken);
                return Results.Ok(new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    Truncated = report.Truncated,
                    Matched = report.Matched.Select(Map).ToList(),
                    ProviderOnly = report.ProviderOnly.Select(Map).ToList(),
                    LocalOnly = report.LocalOnly.Select(Map).ToList()
                });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IShopperOrderNotificationService service)
        => Task.FromResult(Results.Ok());

    private static ReconciliationItemDto Map(NotificationReconciliationItem item) => new()
    {
        ProviderSid = item.ProviderSid,
        Status = item.Status,
        Body = item.Body,
        DateCreated = item.DateCreated,
        DateSent = item.DateSent,
        NotificationId = item.LocalNotificationId,
        Source = item.Source
    };
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public bool Truncated { get; set; }
    public System.Collections.Generic.List<ReconciliationItemDto> Matched { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationItemDto> ProviderOnly { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationItemDto> LocalOnly { get; set; } = new();
}

public class ReconciliationItemDto
{
    public string? ProviderSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? DateCreated { get; set; }
    public string? DateSent { get; set; }
    public int? NotificationId { get; set; }
    public string Source { get; set; } = string.Empty;
}
