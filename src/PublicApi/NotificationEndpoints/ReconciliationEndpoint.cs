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

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service, HttpContext http) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), service, http);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
        => throw new NotSupportedException();

    private async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service, HttpContext http)
    {
        var report = await service.ReconcileAsync(request.From, request.To, http.RequestAborted);
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Matched = report.Matched.Select(Map).ToList(),
            ProviderOnly = report.ProviderOnly.Select(Map).ToList(),
            EshopOnly = report.EshopOnly.Select(Map).ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciledMessageDto Map(ReconciledMessage item)
    {
        return new ReconciledMessageDto
        {
            NotificationId = item.NotificationId,
            ProviderSid = item.ProviderSid,
            Status = item.Status,
            Direction = item.Direction,
            DateSent = item.DateSent,
            ErrorCode = item.ErrorCode,
            ErrorMessage = item.ErrorMessage,
            Source = item.Source
        };
    }
}
