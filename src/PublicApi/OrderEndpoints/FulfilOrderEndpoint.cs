using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: marks the order fulfilled, capturing (taking) the held funds.
/// A stale authorization is renewed first when PayPal still allows it.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderPaymentService) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), orderPaymentService);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new FulfilOrderResponse(request.CorrelationId());

        var payment = await orderPaymentService.FulfilOrderAsync(request.OrderId);

        response.OrderId = request.OrderId;
        response.Payment = PaymentDto.FromPayment(payment);
        return Results.Ok(response);
    }
}

public class FulfilOrderRequest : BaseRequest
{
    public FulfilOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public PaymentDto? Payment { get; set; }
}
