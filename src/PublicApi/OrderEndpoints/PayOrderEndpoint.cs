using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   PayOrderRequest request,
                   IRepository<Order> orderRepo,
                   IRepository<SavedPaymentMethod> pmRepo,
                   PayPalPaymentService paypal,
                   ClaimsPrincipal user,
                   CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var spec = new OrderByIdAndBuyerSpec(orderId, buyerId);
                var order = await orderRepo.FirstOrDefaultAsync(spec, ct);
                if (order == null) return Results.NotFound();

                if (order.Status != OrderStatus.PendingPayment)
                    return Results.Conflict(new { error = $"Order is already in status {order.Status}." });

                if (request.PaymentMethodId == null && request.Card == null)
                    return Results.BadRequest(new { error = "Provide either a saved card (paymentMethodId) or card details." });

                string? vaultTokenId = null;
                if (request.PaymentMethodId.HasValue)
                {
                    var pmSpec = new SavedPaymentMethodByIdAndBuyerSpec(request.PaymentMethodId.Value, buyerId);
                    var pm = await pmRepo.FirstOrDefaultAsync(pmSpec, ct);
                    if (pm == null) return Results.NotFound(new { error = "Saved payment method not found." });
                    vaultTokenId = pm.VaultTokenId;
                }

                AuthorizeResult result;
                try
                {
                    result = await paypal.AuthorizeAsync(
                        orderId: order.Id,
                        amount: order.Total(),
                        cardNumber: request.Card?.Number,
                        expiry: request.Card?.Expiry,
                        cvv: request.Card?.SecurityCode,
                        vaultTokenId: vaultTokenId,
                        ct: ct);
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: ex.HttpStatusCode);
                }

                order.AuthorizePayment(result.PayPalOrderId, result.AuthorizationId);
                await orderRepo.UpdateAsync(order, ct);

                return Results.Ok(new PayOrderResponse
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    AuthorizationId = result.AuthorizationId
                });
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> service)
        => throw new System.NotSupportedException();
}
