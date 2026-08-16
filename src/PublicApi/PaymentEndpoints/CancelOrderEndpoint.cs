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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public PaymentStateDto? Payment { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator cancels before fulfilment; the held funds are
/// released so no money moved. Administrator role only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
                await HandleAsync(new CancelOrderRequest { OrderId = orderId }, paymentService))
            .Produces<CancelOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IPaymentService paymentService)
    {
        try
        {
            var payment = await paymentService.CancelAsync(request.OrderId);
            return Results.Ok(new CancelOrderResponse(request.CorrelationId())
            {
                Payment = PaymentMapper.ToStateDto(payment)
            });
        }
        catch (PaymentNotFoundException ex)
        {
            return PaymentResults.NotFound(ex);
        }
        catch (PaymentException ex)
        {
            return PaymentResults.FromException(ex);
        }
    }
}
