using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Operator action: reconciles PayPal's own transaction records against eShop orders over an ISO-8601 date
/// range, covering the whole range. Restricted to administrators.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, HttpContext http, IPaymentService paymentService) =>
            {
                var request = new ReconciliationRequest { From = from, To = to, Cancellation = http.RequestAborted };
                return await HandleAsync(request, paymentService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService paymentService)
    {
        var report = await paymentService.GetReconciliationAsync(request.From, request.To, request.Cancellation);
        var response = new ReconciliationResponse(request.CorrelationId()) { Report = report };
        return Results.Ok(response);
    }
}

public class ReconciliationRequest : PaymentRequestBase
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(System.Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public ReconciliationReport? Report { get; set; }
}
