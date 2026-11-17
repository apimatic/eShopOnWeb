using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IOrderPaymentService service, CancellationToken cancellationToken) =>
            {
                if (!from.HasValue || !to.HasValue)
                {
                    return Results.BadRequest(new ErrorDetails
                    {
                        StatusCode = StatusCodes.Status400BadRequest,
                        Message = "Both 'from' and 'to' ISO-8601 date-times are required.",
                        Code = "INVALID_DATE_RANGE"
                    });
                }

                var report = await service.ReconcileAsync(from.Value, to.Value, cancellationToken);
                var response = new ReconciliationResponse(System.Guid.NewGuid())
                {
                    From = report.From,
                    To = report.To,
                    FinixTransactionCount = report.FinixTransactionCount,
                    EshopTransactionCount = report.EshopTransactionCount,
                    MatchedCount = report.MatchedCount,
                    FinixOnlyCount = report.FinixOnlyCount,
                    EshopOnlyCount = report.EshopOnlyCount,
                    Entries = report.Entries.Select(PaymentDtoMapper.Map).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderPaymentEndpoints");
    }
}
