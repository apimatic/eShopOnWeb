using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: line Finix transactions up against eShop orders for a
/// date range, including every page of Finix results.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, HttpContext>
{
    private readonly IPaymentService _paymentService;

    public ReconciliationEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(
                Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, HttpContext httpContext) =>
                await HandleAsync(new ReconciliationRequest(from, to), httpContext))
            .Produces<ReconciliationReportDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, HttpContext httpContext)
    {
        if (!request.From.HasValue || !request.To.HasValue)
        {
            throw new PaymentOperationException(
                400,
                "Both 'from' and 'to' ISO-8601 date-times are required.");
        }

        var report = await _paymentService.GetReconciliationAsync(
            request.From.Value,
            request.To.Value,
            httpContext.RequestAborted);

        return Results.Ok(report);
    }
}
