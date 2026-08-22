using System;
using System.Collections.Generic;
using System.Globalization;
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
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationRowDto
{
    public string Sid { get; set; } = string.Empty;
    public string Match { get; set; } = string.Empty;
    public string? EShopStatus { get; set; }
    public string? ProviderStatus { get; set; }
    public int? NotificationId { get; set; }
    public string? DateSent { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconciliationResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationRowDto> Messages { get; set; } = new();
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderMessagingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOrderMessagingService orderMessagingService) =>
            {
                if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromUtc)
                    || !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toUtc))
                {
                    return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
                }

                return await HandleAsync(new ReconciliationRequest(fromUtc, toUtc), orderMessagingService);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderMessagingService orderMessagingService)
    {
        var rows = await orderMessagingService.ReconcileAsync(request.From, request.To, default);
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = request.From,
            To = request.To
        };
        response.Messages.AddRange(rows.Select(r => new ReconciliationRowDto
        {
            Sid = r.Sid,
            Match = r.Match,
            EShopStatus = r.EShopStatus,
            ProviderStatus = r.ProviderStatus,
            NotificationId = r.NotificationId,
            DateSent = r.DateSent
        }));
        return Results.Ok(response);
    }
}
