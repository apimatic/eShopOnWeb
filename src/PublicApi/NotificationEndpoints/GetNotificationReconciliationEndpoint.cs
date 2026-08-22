using System;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class GetNotificationReconciliationEndpoint : IEndpoint<IResult, GetNotificationReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new GetNotificationReconciliationRequest(from, to), service);
            })
            .Produces<GetNotificationReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetNotificationReconciliationRequest request, IOrderNotificationService service)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest("The 'to' timestamp must be on or after 'from'.");
        }

        var report = await service.ReconcileAsync(request.From, request.To);
        return Results.Ok(new GetNotificationReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Matched = report.Matched,
            ProviderOnly = report.ProviderOnly,
            ApplicationOnly = report.ApplicationOnly
        });
    }
}

public class GetNotificationReconciliationRequest : BaseRequest
{
    public GetNotificationReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class GetNotificationReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public System.Collections.Generic.IReadOnlyList<ReconciliationEntry> Matched { get; set; } = System.Array.Empty<ReconciliationEntry>();
    public System.Collections.Generic.IReadOnlyList<ReconciliationEntry> ProviderOnly { get; set; } = System.Array.Empty<ReconciliationEntry>();
    public System.Collections.Generic.IReadOnlyList<ReconciliationEntry> ApplicationOnly { get; set; } = System.Array.Empty<ReconciliationEntry>();
}
