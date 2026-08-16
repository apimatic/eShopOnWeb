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

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public PaymentStateDto? Payment { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/fulfil — operator marks the order fulfilled; this captures the money.
/// A stale authorization is renewed before failing. Administrator role only.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
                await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, paymentService))
            .Produces<FulfilOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IPaymentService paymentService)
    {
        try
        {
            var payment = await paymentService.FulfilAsync(request.OrderId);
            return Results.Ok(new FulfilOrderResponse(request.CorrelationId())
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
