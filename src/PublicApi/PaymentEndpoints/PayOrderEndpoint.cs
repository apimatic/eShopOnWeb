using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderEndpoint.PayOrderRequest>
{
    private readonly IOrderPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IOrderPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int orderId, PayOrderRequest request) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User?.Identity?.Name
            ?? throw new PaymentValidationException("A signed-in shopper is required.");

        var cardRequest = request.Card;
        if (cardRequest is null && !string.IsNullOrWhiteSpace(request.Number))
        {
            cardRequest = new CardRequest
            {
                Number = request.Number,
                ExpirationMonth = request.ExpirationMonth ?? 0,
                ExpirationYear = request.ExpirationYear ?? 0,
                SecurityCode = request.SecurityCode,
                Name = request.Name,
                Address = request.Address
            };
        }

        var card = cardRequest is null
            ? null
            : new FinixCardDetails(
                cardRequest.Number ?? string.Empty,
                cardRequest.ExpirationMonth,
                cardRequest.ExpirationYear,
                cardRequest.SecurityCode,
                cardRequest.Name,
                cardRequest.Address is null
                    ? null
                    : new FinixAddress(
                        cardRequest.Address.Line1,
                        cardRequest.Address.Line2,
                        cardRequest.Address.City,
                        cardRequest.Address.Region,
                        cardRequest.Address.PostalCode,
                        cardRequest.Address.Country));

        var result = await _paymentService.PayAsync(
            new PayOrderCommand(request.OrderId, buyerId, card, request.PaymentMethodId ?? request.SavedPaymentMethodId),
            _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

        return Results.Ok(new PayOrderResponse
        {
            OrderId = result.Order.OrderId,
            IdempotentReplay = result.IdempotentReplay,
            Order = result.Order
        });
    }

    public class PayOrderRequest
    {
        [JsonIgnore]
        public int OrderId { get; set; }
        public CardRequest? Card { get; set; }
        public int? PaymentMethodId { get; set; }
        public int? SavedPaymentMethodId { get; set; }

        // Flat card fields are accepted as a convenience for callers that do not nest them.
        public string? Number { get; set; }
        public int? ExpirationMonth { get; set; }
        public int? ExpirationYear { get; set; }
        public string? SecurityCode { get; set; }
        public string? Name { get; set; }
        public CardAddressRequest? Address { get; set; }
    }

    public class PayOrderResponse
    {
        public int OrderId { get; set; }
        public bool IdempotentReplay { get; set; }
        public OrderView Order { get; set; } = null!;
    }
}

