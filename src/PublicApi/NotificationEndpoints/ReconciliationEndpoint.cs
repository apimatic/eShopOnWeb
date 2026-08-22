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

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IOrderNotificationQueryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationQueryService service) =>
            {
                return await HandleAsync(new ReconciliationQuery(from, to), service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IOrderNotificationQueryService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Matched = report.Matched.Select(ToDto).ToArray(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToArray(),
            ApplicationOnly = report.ApplicationOnly.Select(ToDto).ToArray()
        });
    }

    private static ReconciliationMessageDto ToDto(ReconciledMessage message)
    {
        return new ReconciliationMessageDto
        {
            NotificationId = message.NotificationId,
            ProviderMessageSid = message.ProviderMessageSid,
            DeliveryStatus = message.DeliveryStatus,
            OrderId = message.OrderId,
            Kind = message.Kind,
            DateSent = message.DateSent,
            DateCreated = message.DateCreated
        };
    }
}

public class ReconciliationQuery
{
    public ReconciliationQuery(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}
