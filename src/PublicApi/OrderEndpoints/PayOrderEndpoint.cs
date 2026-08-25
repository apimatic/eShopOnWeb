using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<Order>>
{
    private readonly PayPalService _payPal;

    public PayOrderEndpoint(PayPalService payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   PayOrderRequest request,
                   IRepository<Order> orderRepository,
                   IRepository<SavedPaymentMethod> paymentMethodRepository,
                   ClaimsPrincipal user,
                   CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var spec = new OrderByIdSpec(orderId);
                var order = await orderRepository.FirstOrDefaultAsync(spec, ct);

                if (order == null) return Results.NotFound();
                if (order.BuyerId != buyerId) return Results.Forbid();
                if (order.Status != OrderStatus.PendingPayment)
                    return Results.BadRequest($"Order is in status {order.Status} and cannot be paid.");

                CardDetails? card = null;
                string? vaultTokenId = null;

                if (request.PaymentMethodId.HasValue)
                {
                    var pmSpec = new SavedPaymentMethodByIdAndBuyerSpec(request.PaymentMethodId.Value, buyerId);
                    var pm = await paymentMethodRepository.FirstOrDefaultAsync(pmSpec, ct);
                    if (pm == null) return Results.NotFound("Payment method not found.");
                    vaultTokenId = pm.VaultTokenId;
                }
                else if (request.Card != null)
                {
                    card = new CardDetails(
                        Number: request.Card.Number,
                        Expiry: request.Card.Expiry,
                        SecurityCode: request.Card.SecurityCode,
                        Name: request.Card.Name,
                        BillingCountryCode: request.Card.BillingCountryCode);
                }
                else
                {
                    return Results.BadRequest("Provide either card details or a paymentMethodId.");
                }

                var result = await _payPal.AuthorizeAsync(orderId, order.Total(), card, vaultTokenId, ct);

                order.SetAuthorized(result.PayPalOrderId, result.AuthorizationId);
                await orderRepository.UpdateAsync(order, ct);

                return Results.Ok(new PayOrderResponse
                {
                    OrderId = order.Id,
                    PayPalOrderId = result.PayPalOrderId,
                    AuthorizationId = result.AuthorizationId,
                    Status = order.Status.ToString()
                });
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> dep)
        => Task.FromResult(Results.StatusCode(501));
}

public class PayOrderRequest : BaseRequest
{
    public int? PaymentMethodId { get; set; }
    public CardPaymentRequest? Card { get; set; }
}

public class CardPaymentRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? BillingCountryCode { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse() : base(System.Guid.NewGuid()) { }
    public int OrderId { get; set; }
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
