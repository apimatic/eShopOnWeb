using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total: puts a hold on the money without taking it. Pays either
/// with one-off card details or with one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<Order>, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IRepository<Order> orderRepository, IOrderPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.Username = OrderMapping.GetUserName(user);
                return await HandleAsync(request, orderRepository, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> orderRepository, IOrderPaymentService paymentService)
    {
        if (string.IsNullOrEmpty(request.Username))
        {
            return Results.Unauthorized();
        }
        if (request.Card == null && request.PaymentMethodId == null)
        {
            return Results.BadRequest(new PayOrderResponse { Message = "Provide either card details or a paymentMethodId." });
        }
        if (request.Card != null && request.PaymentMethodId != null)
        {
            return Results.BadRequest(new PayOrderResponse { Message = "Provide card details or a paymentMethodId, not both." });
        }

        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order == null || order.BuyerId != request.Username)
        {
            return Results.NotFound(new PayOrderResponse { Message = $"Order {request.OrderId} was not found." });
        }

        try
        {
            var payment = await paymentService.AuthorizePaymentAsync(order, MapCard(request.Card), request.PaymentMethodId);
            return Results.Ok(new PayOrderResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Payment = OrderMapping.ToDto(payment)
            });
        }
        catch (OrderStateException ex)
        {
            return Results.Conflict(new PayOrderResponse { Message = ex.Message });
        }
        catch (PaymentException ex)
        {
            return Results.UnprocessableEntity(new PayOrderResponse { Message = ex.Message });
        }
    }

    private static CardDetails? MapCard(CardRequest? card) => card == null ? null : new CardDetails
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        CardholderName = card.CardholderName,
        BillingAddressLine1 = card.BillingAddressLine1,
        BillingAddressLine2 = card.BillingAddressLine2,
        BillingCity = card.BillingCity,
        BillingState = card.BillingState,
        BillingPostalCode = card.BillingPostalCode,
        BillingCountryCode = card.BillingCountryCode
    };
}

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string? Username { get; set; }

    /// <summary>One-off card details for this payment.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Id of a saved card (POST /api/payment-methods) to pay with instead.</summary>
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
    public string? Message { get; set; }
}
