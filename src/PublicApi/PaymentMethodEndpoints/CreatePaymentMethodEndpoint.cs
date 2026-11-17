using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : EndpointBaseAsync
    .WithRequest<CreatePaymentMethodRequest>
    .WithActionResult<SavedCardDto>
{
    private readonly IPaymentService _paymentService;

    public CreatePaymentMethodEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost("api/payment-methods")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Save a card for the signed-in shopper",
        Description = "Tokenizes the card at Finix and stores only Finix's payment instrument id and non-sensitive card description.",
        OperationId = "paymentMethods.create",
        Tags = new[] { "PaymentMethodEndpoints" })]
    public override async Task<ActionResult<SavedCardDto>> HandleAsync(
        CreatePaymentMethodRequest request,
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
            if (cardDto == null)
            {
                return BadRequest(new { error = "Card details are required." });
            }

            var savedCard = await _paymentService.SavePaymentMethodAsync(
                buyerId,
                PaymentApiMapper.ToCardInput(cardDto),
                cancellationToken);

            return Created($"api/payment-methods/{savedCard.PaymentMethodId}", savedCard);
        }
        catch (System.Exception ex)
        {
            return PaymentApiMapper.ErrorResult(ex);
        }
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDto? Card { get; set; }

    public string? Number { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardAddressDto? Address { get; set; }
}
