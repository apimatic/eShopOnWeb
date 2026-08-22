using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest(from, to), service, cancellationToken);
            })
            .Produces<ReconcileNotificationsResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconcileNotificationsRequest request, IOrderNotificationService service)
        => HandleAsync(request, service, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        ReconcileNotificationsRequest request,
        IOrderNotificationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await service.ReconcileAsync(request.From, request.To, cancellationToken);
            return Results.Ok(new ReconcileNotificationsResponse
            {
                From = report.From,
                To = report.To,
                SendingNumber = report.SendingNumber,
                Matched = report.Matched,
                ProviderOnly = report.ProviderOnly,
                LocalOnly = report.LocalOnly,
                Truncated = report.Truncated
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}

public class ReconcileNotificationsRequest : BaseRequest
{
    public ReconcileNotificationsRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconcileNotificationsResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string SendingNumber { get; set; } = string.Empty;
    public IReadOnlyList<ReconciliationRow> Matched { get; set; } = Array.Empty<ReconciliationRow>();
    public IReadOnlyList<ReconciliationRow> ProviderOnly { get; set; } = Array.Empty<ReconciliationRow>();
    public IReadOnlyList<ReconciliationRow> LocalOnly { get; set; } = Array.Empty<ReconciliationRow>();
    public bool Truncated { get; set; }
}
