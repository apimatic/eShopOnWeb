using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>One-off card details for this payment. Mutually exclusive with PaymentMethodId.</summary>
    public CardDetailsRequest? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards. Mutually exclusive with Card.</summary>
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentStateDto Payment { get; set; } = new();
}

/// <summary>
/// Authorizes (holds) the order total via PayPal, with either one-off card details
/// or one of the shopper's saved cards. No money is taken until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal>
{
    private readonly IPaymentService _paymentService;

    public PayOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var order = await _paymentService.PayOrderAsync(
                buyerId, request.OrderId, request.Card?.ToCardDetails(), request.PaymentMethodId);

            var dto = OrderDto.FromOrder(order);
            return Results.Ok(new PayOrderResponse(request.CorrelationId())
            {
                OrderId = dto.OrderId,
                Status = dto.Status,
                Payment = dto.Payment
            });
        }
        catch (Exception ex) when (PaymentEndpointHelpers.TryMapException(ex) is { } result)
        {
            return result;
        }
    }
}
