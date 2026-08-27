using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels the order before fulfilment, releasing the shopper's held funds.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromRoute] int orderId, IPaymentService paymentService) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, paymentService);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IPaymentService paymentService)
    {
        var payment = await paymentService.CancelOrderAsync(request.OrderId);

        var response = new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Status = "Cancelled",
            Payment = payment is null ? null : PaymentDto.FromEntity(payment)
        };
        return Results.Ok(response);
    }
}

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
}
