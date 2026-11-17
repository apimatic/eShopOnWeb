using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) {}
    public ReconciliationResponse() {}

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransaction> FinixTransactions { get; set; } = new();
    public List<ReconciliationTransaction> UnmatchedFinixTransactions { get; set; } = new();
    public List<ReconciliationOrder> UnmatchedOrders { get; set; } = new();
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IPaymentService paymentService) =>
            {
                if (!from.HasValue || !to.HasValue)
                {
                    throw new PaymentValidationException(
                        "Both ISO-8601 'from' and 'to' query parameters are required.");
                }

                return await HandleAsync(new ReconciliationRequest
                {
                    From = from.Value,
                    To = to.Value
                }, paymentService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService paymentService)
    {
        var report = await paymentService.GetReconciliationAsync(request.From, request.To);
        return Results.Ok(new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            FinixTransactions = report.FinixTransactions,
            UnmatchedFinixTransactions = report.UnmatchedFinixTransactions,
            UnmatchedOrders = report.UnmatchedOrders
        });
    }
}
