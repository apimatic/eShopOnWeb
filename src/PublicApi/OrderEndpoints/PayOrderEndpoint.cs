using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly IRepository<ApplicationCore.Entities.OrderAggregate.Order> _orderRepository;

    public PayOrderEndpoint(IRepository<ApplicationCore.Entities.OrderAggregate.Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService)
    {
        if (request.Card is not null)
        {
            ValidateCard(request.Card);
        }

        var payment = await paymentService.PayAsync(
            request.OrderId,
            request.BuyerId,
            request.Card is null ? null : PaymentMapping.ToCardPaymentSource(request.Card),
            request.PaymentMethodId);

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpecification(request.OrderId));
        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Order = PaymentDtoFactory.From(order!, payment)
        };
        return Results.Ok(response);
    }

    private static void ValidateCard(CardRequest card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentException(400, "Card number and expiry (YYYY-MM) are required.");
        }
    }
}

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }

    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}
