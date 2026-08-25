using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<Order>>
{
    private readonly IPaymentService _paymentService;
    private readonly string _currency;

    public PayOrderEndpoint(IPaymentService paymentService, IConfiguration config)
    {
        _paymentService = paymentService;
        _currency = config["PayPal:Currency"] ?? "USD";
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, [FromBody] PayOrderRequest request, IRepository<Order> orderRepo, HttpContext httpContext) =>
            {
                request.OrderId = orderId;
                request.BuyerId = httpContext.User.Identity!.Name!;
                return await HandleAsync(request, orderRepo);
            })
            .Produces<PayOrderResponse>()
            .ProducesProblem(400)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> orderRepo)
    {
        if (request.Card is null && string.IsNullOrWhiteSpace(request.VaultToken))
            return Results.BadRequest("Either card details or a vault token must be provided.");
        if (request.Card is not null && !string.IsNullOrWhiteSpace(request.VaultToken))
            return Results.BadRequest("Provide either card details or a vault token, not both.");

        var order = await orderRepo.GetByIdAsync(request.OrderId);
        if (order is null)
            return Results.NotFound();
        if (order.BuyerId != request.BuyerId)
            return Results.Forbid();
        if (order.PaymentStatus != OrderPaymentStatus.Pending)
            return Results.BadRequest($"Order is in '{order.PaymentStatus}' state and cannot be paid.");

        CardDetails? card = null;
        if (request.Card is not null)
        {
            card = new CardDetails(request.Card.Number, request.Card.Expiry, request.Card.Cvv, request.Card.Name);
        }

        var result = await _paymentService.AuthorizePaymentAsync(
            order.Total(), _currency, card, request.VaultToken, order.Id.ToString());

        order.SetPaymentAuthorized(result.PayPalOrderId, result.AuthorizationId,
            result.AuthorizationExpiry, result.AuthorizationCreatedAt);
        await orderRepo.UpdateAsync(order);

        return Results.Ok(new PayOrderResponse(request.CorrelationId())
        {
            PayPalOrderId = result.PayPalOrderId,
            AuthorizationId = result.AuthorizationId,
            ExpiresAt = result.AuthorizationExpiry
        });
    }
}
