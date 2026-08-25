using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest
{
    public string? CardNumber { get; set; }
    public string? CardExpiry { get; set; }
    public string? CardCvc { get; set; }
    public string? CardHolderName { get; set; }
    public string? BillingCountryCode { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public int? SavedCardId { get; set; }
}

public class PayOrderResponse
{
    public string PaymentStatus { get; set; } = string.Empty;
    public string PayPalAuthorizationId { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpiry { get; set; }
}

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, HttpContext ctx) =>
            {
                return await HandleAsync(request, ctx, orderId);
            })
            .Produces<PayOrderResponse>()
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, HttpContext ctx)
        => HandleAsync(request, ctx, 0);

    private async Task<IResult> HandleAsync(PayOrderRequest request, HttpContext ctx, int orderId)
    {
        var buyerId = ctx.User.FindFirstValue(ClaimTypes.Name)!;
        var sp = ctx.RequestServices;
        var orderRepo = sp.GetRequiredService<IReadRepository<Order>>();
        var paymentRepo = sp.GetRequiredService<IRepository<Payment>>();
        var savedCardRepo = sp.GetRequiredService<IReadRepository<SavedCard>>();
        var paypalService = sp.GetRequiredService<IPayPalService>();
        var ct = ctx.RequestAborted;

        var order = await orderRepo.GetByIdAsync(orderId, ct);
        if (order is null) return Results.NotFound("Order not found.");
        if (order.BuyerId != buyerId) return Results.Forbid();

        var paymentSpec = new PaymentByOrderIdSpec(orderId);
        var payment = await paymentRepo.FirstOrDefaultAsync(paymentSpec, ct);
        if (payment is null) return Results.NotFound("Payment record not found.");
        if (payment.Status != PaymentStatus.PendingPayment)
            return Results.Conflict($"Order is already in status: {payment.Status}");

        var idempotencyKey = $"pay-{payment.Id}-{Guid.NewGuid():N}";
        AuthorizeResult authResult;

        try
        {
            if (request.SavedCardId.HasValue)
            {
                var savedCard = await savedCardRepo.GetByIdAsync(request.SavedCardId.Value, ct);
                if (savedCard is null) return Results.NotFound("Saved card not found.");
                if (savedCard.BuyerId != buyerId) return Results.Forbid();

                authResult = await paypalService.CreateAndAuthorizeWithVaultAsync(
                    amount: payment.Amount,
                    currency: payment.Currency,
                    eShopOrderId: orderId.ToString(),
                    idempotencyKey: idempotencyKey,
                    vaultTokenId: savedCard.VaultTokenId,
                    ct: ct);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.CardNumber) ||
                    string.IsNullOrWhiteSpace(request.CardExpiry) ||
                    string.IsNullOrWhiteSpace(request.BillingCountryCode))
                {
                    return Results.BadRequest("Either savedCardId or card details (cardNumber, cardExpiry, billingCountryCode) are required.");
                }

                var cardDetails = new DirectCardDetails(
                    Number: request.CardNumber,
                    Expiry: request.CardExpiry,
                    SecurityCode: request.CardCvc ?? string.Empty,
                    Name: request.CardHolderName,
                    CountryCode: request.BillingCountryCode,
                    AddressLine1: request.BillingAddressLine1,
                    City: request.BillingCity,
                    State: request.BillingState,
                    PostalCode: request.BillingPostalCode);

                authResult = await paypalService.CreateAndAuthorizeAsync(
                    amount: payment.Amount,
                    currency: payment.Currency,
                    eShopOrderId: orderId.ToString(),
                    idempotencyKey: idempotencyKey,
                    card: cardDetails,
                    ct: ct);
            }
        }
        catch (PayPalException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode ?? 422);
        }

        payment.RecordAuthorization(authResult.PayPalOrderId, authResult.AuthorizationId, authResult.AuthorizationExpiry);
        await paymentRepo.UpdateAsync(payment, ct);

        return Results.Ok(new PayOrderResponse
        {
            PaymentStatus = payment.Status.ToString(),
            PayPalAuthorizationId = authResult.AuthorizationId,
            AuthorizationExpiry = authResult.AuthorizationExpiry
        });
    }
}
