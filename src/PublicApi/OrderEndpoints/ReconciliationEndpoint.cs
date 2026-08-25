using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationRequest
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPayPalPaymentService>
{
    private readonly IReadRepository<OrderPayment> _paymentRepository;

    public ReconciliationEndpoint(IReadRepository<OrderPayment> paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Administrators")]
            async (string from, string to, IPayPalPaymentService paymentService) =>
            {
                var request = new ReconciliationRequest { From = from ?? string.Empty, To = to ?? string.Empty };
                return await HandleAsync(request, paymentService);
            })
            .Produces<object>(200)
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPayPalPaymentService paymentService)
    {
        if (string.IsNullOrEmpty(request.From) || string.IsNullOrEmpty(request.To))
            return Results.BadRequest(new { error = "Both 'from' and 'to' query parameters are required (ISO-8601 dates)." });

        // Validate and normalize date formats — PayPal requires full ISO-8601 datetimes with timezone
        if (!DateTimeOffset.TryParse(request.From, out var fromDate) || !DateTimeOffset.TryParse(request.To, out var toDate))
            return Results.BadRequest(new { error = "'from' and 'to' must be valid ISO-8601 date or datetime values." });

        // PayPal expects ISO-8601 with timezone offset without colons, e.g. "2025-01-01T00:00:00+0000"
        var fromStr = fromDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss") + "+0000";
        var toStr = toDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss") + "+0000";

        var allPayments = await _paymentRepository.ListAsync();

        IReadOnlyList<TransactionMatch> matches;
        try
        {
            matches = await paymentService.ReconcileAsync(fromStr, toStr, allPayments, CancellationToken.None);
        }
        catch (PayPalException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode);
        }

        return Results.Ok(new { from = request.From, to = request.To, transactions = matches });
    }
}
