using System;
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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Authorizes the order total (places a hold; no money is taken) using either one-off card details
/// or one of the shopper's saved cards. Shopper-scoped: acts only on the caller's own order.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal>
{
    private readonly IPaymentService _paymentService;
    private readonly IRepository<Order> _orderRepository;

    public PayOrderEndpoint(IPaymentService paymentService, IRepository<Order> orderRepository)
    {
        _paymentService = paymentService;
        _orderRepository = orderRepository;
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
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user)
    {
        var response = new PayOrderResponse(request.CorrelationId());
        var buyerId = CallerIdentity.GetBuyerId(user);

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null || order.BuyerId != buyerId)
        {
            throw new EntityNotFoundException($"Order {request.OrderId} was not found.");
        }

        var instruction = new PaymentInstruction
        {
            SavedPaymentMethodId = request.SavedPaymentMethodId,
            Card = request.SavedPaymentMethodId.HasValue ? null : request.Card?.ToCardDetails()
        };

        await _paymentService.AuthorizeOrderAsync(order, instruction);

        response.OrderId = order.Id;
        response.Order = OrderDto.From(order);
        return Results.Ok(response);
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Card details for a one-off payment. Ignored when <see cref="SavedPaymentMethodId"/> is set.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;   // YYYY-MM
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BillingAddressRequest? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        Number,
        Expiry,
        SecurityCode,
        Name,
        BillingAddress?.ToCardBillingAddress());
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }   // city
    public string? AdminArea1 { get; set; }   // state / province
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public CardBillingAddress ToCardBillingAddress() =>
        new(AddressLine1, AddressLine2, AdminArea2, AdminArea1, PostalCode, CountryCode);
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public OrderDto? Order { get; set; }
}
