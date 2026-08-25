using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<Order>>
{
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedMethodRepository;
    private readonly PayPalClient _payPalClient;
    private readonly PayPalSettings _payPalSettings;

    public PayOrderEndpoint(
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedMethodRepository,
        PayPalClient payPalClient,
        Microsoft.Extensions.Options.IOptions<PayPalSettings> payPalSettings)
    {
        _paymentRepository = paymentRepository;
        _savedMethodRepository = savedMethodRepository;
        _payPalClient = payPalClient;
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IRepository<Order> orderRepository, ClaimsPrincipal user) =>
            {
                var buyerId = user.FindFirst(ClaimTypes.Name)?.Value ?? "";
                return await HandleAsync(request with { OrderId = orderId, BuyerId = buyerId }, orderRepository);
            })
            .Produces<PayOrderResponse>(200)
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> orderRepository)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var orderSpec = new OrderByIdWithItemsSpec(request.OrderId);
        var order = await orderRepository.FirstOrDefaultAsync(orderSpec);
        if (order == null || order.BuyerId != request.BuyerId)
            return Results.NotFound(new { error = "Order not found." });

        if (order.Status != OrderStatus.PendingPayment)
        {
            // Idempotency: if already authorized, return existing
            if (order.Status == OrderStatus.PaymentAuthorized)
            {
                var existingPayment = await _paymentRepository.FirstOrDefaultAsync(
                    new PaymentByOrderIdSpec(request.OrderId));
                if (existingPayment != null)
                    return Results.Ok(new PayOrderResponse { AuthorizationId = existingPayment.AuthorizationId, Status = "PaymentAuthorized" });
            }
            return Results.Conflict(new { error = $"Order is in status {order.Status} and cannot be paid." });
        }

        // Validate exactly one payment source
        bool hasCard = !string.IsNullOrEmpty(request.CardNumber);
        bool hasSaved = request.SavedPaymentMethodId.HasValue;
        if (hasCard == hasSaved)
            return Results.BadRequest(new { error = "Provide either card details or a savedPaymentMethodId, not both." });

        var total = order.Total();
        if (total <= 0)
            return Results.BadRequest(new { error = "Order total must be greater than zero." });

        var idempotencyKey = Guid.NewGuid().ToString();

        try
        {
            PayPalOrderResponse payPalOrder;

            if (hasCard)
            {
                var billingAddress = new PayPalAddress
                {
                    CountryCode = request.BillingCountryCode ?? "US",
                    AddressLine1 = request.BillingStreet,
                    City = request.BillingCity,
                    State = request.BillingState,
                    PostalCode = request.BillingPostalCode
                };

                payPalOrder = await _payPalClient.CreateOrderWithCardAsync(
                    total, _payPalSettings.Currency, order.Id,
                    request.CardNumber!, request.CardExpiry!, request.CardCvv!,
                    request.CardName ?? "", billingAddress, idempotencyKey);
            }
            else
            {
                var savedMethod = await _savedMethodRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodsByBuyerSpec(request.BuyerId));

                // Find the specific saved method by ID
                var savedMethods = await _savedMethodRepository.ListAsync(
                    new SavedPaymentMethodsByBuyerSpec(request.BuyerId));
                var method = savedMethods.FirstOrDefault(m => m.Id == request.SavedPaymentMethodId!.Value);

                if (method == null)
                    return Results.NotFound(new { error = "Saved payment method not found." });

                payPalOrder = await _payPalClient.CreateOrderWithVaultAsync(
                    total, _payPalSettings.Currency, order.Id,
                    method.PayPalVaultTokenId, idempotencyKey);
            }

            // Extract authorization ID from response
            var auth = payPalOrder.PurchaseUnits
                .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

            if (auth == null || string.IsNullOrEmpty(auth.Id))
                return Results.Problem("PayPal did not return an authorization ID. Payment may not have been processed.");

            var payment = new Payment(
                order.Id, request.BuyerId, payPalOrder.Id, auth.Id,
                total, _payPalSettings.Currency, idempotencyKey);

            await _paymentRepository.AddAsync(payment);

            order.SetStatus(OrderStatus.PaymentAuthorized);
            await orderRepository.UpdateAsync(order);

            return Results.Ok(new PayOrderResponse
            {
                AuthorizationId = auth.Id,
                Status = "PaymentAuthorized"
            });
        }
        catch (PayerActionRequiredException ex)
        {
            return Results.Problem(
                detail: $"PayPal requires browser-based payer action (3DS or similar): {ex.ApprovalUrl}",
                statusCode: 422,
                title: "PayerActionRequired");
        }
        catch (PayPalException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: ex.StatusCode > 0 ? ex.StatusCode : 502,
                title: "PayPalError",
                extensions: ex.DebugId != null ? new System.Collections.Generic.Dictionary<string, object?> { ["debugId"] = ex.DebugId } : null);
        }
    }
}
