using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPayPalService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, HttpContext ctx,
                   IRepository<Order> orderRepo,
                   IRepository<OrderPayment> paymentRepo,
                   IRepository<SavedPaymentMethod> methodRepo,
                   IPayPalService paypal) =>
            {
                var username = ctx.User.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(username)) return Results.Unauthorized();

                var order = await orderRepo.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
                if (order == null) return Results.NotFound($"Order {orderId} not found.");
                if (order.BuyerId != username)
                    return Results.Problem("Access denied.", statusCode: 403);

                // Idempotency: if already authorized, return existing state
                var existingPayment = await paymentRepo.FirstOrDefaultAsync(
                    new OrderPaymentByOrderIdSpec(orderId));
                if (existingPayment?.Status == PaymentStatus.Authorized)
                    return Results.Ok(new PayOrderResponse
                    {
                        OrderId = orderId,
                        AuthorizationId = existingPayment.PayPalAuthorizationId!,
                        Status = existingPayment.Status.ToString(),
                        Amount = existingPayment.Amount,
                        Currency = existingPayment.Currency
                    });

                if (existingPayment != null && existingPayment.Status != PaymentStatus.PendingPayment)
                    return Results.Problem(
                        $"Order is in state '{existingPayment.Status}' and cannot be paid.",
                        statusCode: 409);

                var currency = request.Currency ?? "USD";
                var total = order.Total();
                var idempotencyKey = Guid.NewGuid().ToString("N");

                try
                {
                    AuthorizeResult authResult;

                    if (!string.IsNullOrEmpty(request.PaymentMethodId))
                    {
                        // Use saved card
                        var savedMethod = await methodRepo.GetByIdAsync(int.Parse(request.PaymentMethodId));
                        if (savedMethod == null || savedMethod.BuyerIdentityGuid != username)
                            return Results.Problem("Payment method not found.", statusCode: 404);

                        authResult = await paypal.AuthorizeWithVaultAsync(
                            idempotencyKey, total, currency, savedMethod.PayPalVaultId);
                    }
                    else if (request.CardDetails != null)
                    {
                        var card = new CardDetails(
                            request.CardDetails.CardNumber,
                            request.CardDetails.ExpiryYear,
                            request.CardDetails.ExpiryMonth,
                            request.CardDetails.Cvv,
                            request.CardDetails.CardholderName,
                            request.CardDetails.BillingAddress?.Street,
                            request.CardDetails.BillingAddress?.City,
                            request.CardDetails.BillingAddress?.State,
                            request.CardDetails.BillingAddress?.Country,
                            request.CardDetails.BillingAddress?.ZipCode);

                        authResult = await paypal.AuthorizeWithCardAsync(
                            idempotencyKey, total, currency, card, username);
                    }
                    else
                    {
                        return Results.BadRequest(
                            "Provide either 'cardDetails' or 'paymentMethodId'.");
                    }

                    // Persist payment record
                    var payment = new OrderPayment(orderId, authResult.PayPalOrderId, total, currency);
                    payment.SetAuthorized(authResult.AuthorizationId);
                    await paymentRepo.AddAsync(payment);

                    return Results.Ok(new PayOrderResponse
                    {
                        OrderId = orderId,
                        AuthorizationId = authResult.AuthorizationId,
                        Status = payment.Status.ToString(),
                        Amount = total,
                        Currency = currency
                    });
                }
                catch (PayPalException ex) when (ex.IsPayerActionRequired)
                {
                    return Results.Problem(
                        "PayPal requires 3DS payer action for this card. " +
                        "Direct server-to-server card authorization is not supported for this card. " +
                        "Use a different card or a pre-vaulted method.",
                        statusCode: 422);
                }
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IPayPalService dependency)
        => throw new NotImplementedException();
}

public class PayOrderRequest
{
    public CardPaymentDetails? CardDetails { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? Currency { get; set; }
}

public class CardPaymentDetails
{
    public string CardNumber { get; set; } = "";
    public int ExpiryYear { get; set; }
    public int ExpiryMonth { get; set; }
    public string Cvv { get; set; } = "";
    public string CardholderName { get; set; } = "";
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public string AuthorizationId { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
}
