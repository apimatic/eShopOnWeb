using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total with PayPal (a hold; no money moves yet).
/// Pays either with one-off card details or with one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;

    public PayOrderEndpoint(IRepository<Order> orderRepository, IRepository<SavedPaymentMethod> paymentMethodRepository)
    {
        _orderRepository = orderRepository;
        _paymentMethodRepository = paymentMethodRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null || order.BuyerId != request.BuyerId)
        {
            return Results.NotFound();
        }

        SavedPaymentMethod? savedPaymentMethod = null;
        if (request.SavedPaymentMethodId.HasValue)
        {
            savedPaymentMethod = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(request.SavedPaymentMethodId.Value, request.BuyerId));
            if (savedPaymentMethod is null)
            {
                return Results.NotFound($"Saved payment method {request.SavedPaymentMethodId} was not found.");
            }
        }
        else if (request.Card is not null)
        {
            var validationError = request.Card.Validate();
            if (validationError is not null)
            {
                return Results.BadRequest(validationError);
            }
        }
        else
        {
            return Results.BadRequest("Supply either card details or a savedPaymentMethodId.");
        }

        var payment = await paymentService.AuthorizePaymentAsync(order, request.Card?.ToPayPalCardDetails(), savedPaymentMethod);

        response.OrderId = order.Id;
        response.PaymentId = payment.Id;
        response.Status = order.Status.ToString();
        response.AuthorizationId = payment.AuthorizationId;
        response.AuthorizationStatus = payment.AuthorizationStatus;
        response.AuthorizedAmount = payment.AuthorizedAmount;
        response.Currency = payment.Currency;
        return Results.Ok(response);
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public CardDetailsRequest? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public decimal AuthorizedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
