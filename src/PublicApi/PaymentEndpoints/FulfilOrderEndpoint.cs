using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: marks the order fulfilled and captures the money. Afterwards the payment shows the captured
/// amount, PayPal's fee and the net proceeds. A stale authorization is renewed before capture.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderOperationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
            {
                return await HandleAsync(new OrderOperationRequest { OrderId = orderId }, service);
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderOperationRequest request, IOrderPaymentService service)
    {
        var payment = await service.FulfilAsync(request.OrderId);
        return Results.Ok(new OrderPaymentResponse(request.CorrelationId()) { Payment = PaymentMappers.ToDto(payment) });
    }
}

public class OrderOperationRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class OrderPaymentResponse : BaseResponse
{
    public OrderPaymentResponse(Guid correlationId) : base(correlationId) { }
    public OrderPaymentResponse() { }

    public OrderPaymentDto Payment { get; set; } = new();
}
