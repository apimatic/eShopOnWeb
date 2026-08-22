using System;
using System.Linq;
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

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IShopOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IShopOrderService service) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromValue) || !DateTimeOffset.TryParse(to, out var toValue))
                {
                    throw new ClientRequestException("from and to must be ISO-8601 date-times.");
                }

                return await HandleAsync(new ReconciliationRequest { From = fromValue, To = toValue }, service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IShopOrderService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            ProviderCount = report.Matched.Count + report.ProviderOnly.Count,
            ApplicationCount = report.Matched.Count + report.ApplicationOnly.Count,
            Matched = report.Matched.ToList(),
            ProviderOnly = report.ProviderOnly.ToList(),
            ApplicationOnly = report.ApplicationOnly.ToList()
        };

        return Results.Ok(response);
    }
}
