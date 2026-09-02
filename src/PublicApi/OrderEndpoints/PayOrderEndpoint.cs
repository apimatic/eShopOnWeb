using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total via PayPal, either with one-off card details
/// or with one of the shopper's saved cards. No money is captured yet.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, int, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPaymentService _paymentService;

    public PayOrderEndpoint(IRepository<Order> orderRepository, IPaymentService paymentService)
    {
        _orderRepository = orderRepository;
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PayOrderRequest request, int orderId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, orderId, user);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, int orderId, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null || order.BuyerId != buyerId)
        {
            return Results.NotFound($"Order {orderId} was not found.");
        }

        if (request.Card == null && !request.PaymentMethodId.HasValue)
        {
            return Results.BadRequest("Provide either card details or a paymentMethodId of a saved card.");
        }

        CardDetails? card = null;
        if (request.Card != null)
        {
            if (string.IsNullOrWhiteSpace(request.Card.Number) || string.IsNullOrWhiteSpace(request.Card.SecurityCode))
            {
                return Results.BadRequest("Card number and security code are required.");
            }
            card = new CardDetails
            {
                Number = request.Card.Number,
                ExpiryMonth = request.Card.ExpiryMonth,
                ExpiryYear = request.Card.ExpiryYear,
                SecurityCode = request.Card.SecurityCode,
                CardholderName = request.Card.CardholderName ?? string.Empty,
                BillingAddressLine1 = request.Card.BillingAddressLine1,
                BillingAddressLine2 = request.Card.BillingAddressLine2,
                BillingCity = request.Card.BillingCity,
                BillingState = request.Card.BillingState,
                BillingPostalCode = request.Card.BillingPostalCode,
                BillingCountryCode = request.Card.BillingCountryCode
            };
        }

        try
        {
            var payment = await _paymentService.AuthorizePaymentAsync(order, card, request.PaymentMethodId);
            var response = new PayOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Payment = PaymentDto.FromPayment(payment)
            };
            return Results.Ok(response);
        }
        catch (PaymentDeclinedException ex)
        {
            return Results.UnprocessableEntity(new { message = ex.Message });
        }
        catch (InvalidPaymentStateException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}
