using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderRequest : BaseRequest
{
    public FulfilOrderRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public PaymentSummaryDto Payment { get; set; } = new();
}

/// <summary>
/// Operator action: fulfils the order and captures the held funds — this is when money is taken.
/// A hold that has gone stale is renewed first; one that can no longer be renewed surfaces an
/// operator-actionable message. Restricted to administrators. Idempotent.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), service);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService service)
    {
        try
        {
            var order = await service.FulfilAsync(request.OrderId);
            var response = new FulfilOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Payment = OrderMapping.ToPaymentSummary(order.Payment!)
            };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return PaymentProblem.ToResult(ex);
        }
    }
}
