using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : EndpointBaseAsync
    .WithRequest<PayOrderRequest>
    .WithActionResult<OrderDto>
{
    private readonly IPaymentService _paymentService;

    public PayOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Authorize payment for an order",
        Description = "Places a hold on the order total using one-off card details or one of the shopper's saved cards. Funds are not captured.",
        OperationId = "orders.pay",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<OrderDto>> HandleAsync(
        PayOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var buyerId = PaymentApiMapper.BuyerId(User);
            var cardDto = PaymentApiMapper.ResolveCard(
                request.Card,
                request.Number,
                request.ExpirationMonth,
                request.ExpirationYear,
                request.SecurityCode,
                request.Name,
                request.Address);
            var card = cardDto == null ? null : PaymentApiMapper.ToCardInput(cardDto);
            var orderId = PaymentApiMapper.RouteId(RouteData, "orderId");

            var order = await _paymentService.PayOrderAsync(
                buyerId,
                orderId,
                card,
                request.PaymentMethodId ?? request.SavedPaymentMethodId,
                cancellationToken);

            return Ok(order);
        }
        catch (System.Exception ex)
        {
            return PaymentApiMapper.ErrorResult(ex);
        }
    }
}

public class PayOrderRequest : BaseRequest
{
    public CardDto? Card { get; set; }

    public int? PaymentMethodId { get; set; }

    public int? SavedPaymentMethodId { get; set; }

    public string? Number { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardAddressDto? Address { get; set; }
}
