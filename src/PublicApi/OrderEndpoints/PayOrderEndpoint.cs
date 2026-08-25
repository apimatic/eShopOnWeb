using System.Security.Claims;
using System.Threading;
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
using Microsoft.eShopWeb.PublicApi.PayPalService;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public string? CardNumber { get; set; }
    public string? CardExpiry { get; set; }
    public string? CardSecurityCode { get; set; }
    public string? CardHolderName { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public string Status { get; set; } = "";
    public string AuthorizationId { get; set; } = "";
}

public class PayOrderEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IRepository<Order> orderRepo,
                   IRepository<Buyer> buyerRepo, IRepository<Payment> paymentRepo,
                   IPayPalService paypal, HttpContext httpContext, CancellationToken ct) =>
            {
                var buyerId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var order = await orderRepo.FirstOrDefaultAsync(new OrderWithPaymentByBuyerSpec(orderId, buyerId), ct);
                if (order == null) return Results.NotFound("Order not found.");
                if (order.Status != OrderStatus.AwaitingPayment)
                    return Results.BadRequest($"Order is already in status '{order.Status}'.");

                CardPaymentDetails? cardDetails = null;
                string? vaultId = null;

                if (request.SavedPaymentMethodId.HasValue)
                {
                    var buyer = await buyerRepo.FirstOrDefaultAsync(new BuyerByIdentitySpec(buyerId), ct);
                    var pm = buyer?.FindPaymentMethod(request.SavedPaymentMethodId.Value);
                    if (pm == null) return Results.BadRequest("Saved payment method not found.");
                    vaultId = pm.CardId;
                }
                else if (!string.IsNullOrEmpty(request.CardNumber))
                {
                    if (string.IsNullOrEmpty(request.CardExpiry) || string.IsNullOrEmpty(request.CardSecurityCode))
                        return Results.BadRequest("Card expiry and security code are required.");
                    cardDetails = new CardPaymentDetails(
                        request.CardNumber, request.CardExpiry, request.CardSecurityCode, request.CardHolderName);
                }
                else
                {
                    return Results.BadRequest("Provide card details or a saved payment method ID.");
                }

                var currency = paypal.Currency;
                var paypalOrderId = await paypal.CreatePayPalOrderAsync(order.Total(), currency, ct);
                var authId = await paypal.AuthorizeOrderAsync(paypalOrderId, $"auth-{order.Id}", cardDetails, vaultId, ct);

                var payment = new Payment(order.Id, paypalOrderId, authId, currency);
                await paymentRepo.AddAsync(payment, ct);
                order.SetPaymentAuthorized(payment);
                await orderRepo.UpdateAsync(order, ct);

                return Results.Ok(new PayOrderResponse(request.CorrelationId())
                {
                    Status = order.Status.ToString(),
                    AuthorizationId = authId
                });
            })
            .Produces<PayOrderResponse>()
            .Produces(400).Produces(404)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.StatusCode(501));
}
