using System;
using System.Globalization;
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

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        if (!DateTimeOffset.TryParse(request.From, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var from)
            || !DateTimeOffset.TryParse(request.To, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var to))
        {
            throw new PaymentException("from and to must be ISO-8601 date-times.", 400);
        }

        var report = await service.ReconcileAsync(from, to, default);
        return Results.Ok(new
        {
            from = report.From,
            to = report.To,
            rows = report.Rows.Select(r => new
            {
                r.MatchStatus,
                r.OrderId,
                r.PayPalTransactionId,
                r.EshopAmount,
                r.PayPalAmount,
                r.Currency,
                r.PayPalStatus,
                r.OrderPaymentStatus,
                r.Timestamp,
                r.Notes
            }).ToList()
        });
    }
}
