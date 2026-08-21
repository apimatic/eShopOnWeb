using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentService;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and lines them up
/// against eShop orders, over the whole range (paginated), so a payment one side knows about and the
/// other doesn't is visible. <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentService paymentService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), paymentService);
            })
            .Produces<ReconciliationReport>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService paymentService)
    {
        var report = await paymentService.ReconcileAsync(request.From, request.To);
        return Results.Ok(report);
    }
}

public class ReconciliationRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}
