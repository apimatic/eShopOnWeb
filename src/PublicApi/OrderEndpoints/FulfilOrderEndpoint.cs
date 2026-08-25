using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

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
    public PaymentDto Payment { get; set; } = new();
}

/// <summary>
/// Operator action: captures a previously-authorized order's payment. Renews a stale authorization
/// automatically; if it can no longer be renewed, returns 422 with an operator-actionable message.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), paymentService);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService paymentService)
    {
        var response = new FulfilOrderResponse(request.CorrelationId()) { OrderId = request.OrderId };

        try
        {
            var payment = await paymentService.FulfilOrderAsync(request.OrderId);
            response.Payment = PaymentDto.FromEntity(payment);
            return Results.Ok(response);
        }
        catch (Exception ex) when (ex is OrderNotFoundException or InvalidOrderStateException
            or AuthorizationExpiredException or AuthorizationNotRenewableException or PaymentGatewayException)
        {
            return PaymentExceptionResults.Map(ex);
        }
    }
}
