using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, HttpContext ctx,
                   IRepository<Order> orderRepo,
                   IRepository<PaymentRecord> paymentRepo,
                   IRepository<SavedCard> savedCardRepo,
                   IPayPalService payPalService,
                   PayPalSettings settings) =>
            {
                request.OrderId = orderId;
                request.BuyerId = ctx.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, orderRepo, paymentRepo, savedCardRepo, payPalService, settings);
            })
            .Produces(200)
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> repo)
        => throw new System.NotSupportedException();

    private static async Task<IResult> HandleAsync(
        PayOrderRequest request,
        IRepository<Order> orderRepo,
        IRepository<PaymentRecord> paymentRepo,
        IRepository<SavedCard> savedCardRepo,
        IPayPalService payPalService,
        PayPalSettings settings)
    {
        if (string.IsNullOrEmpty(request.BuyerId)) return Results.Unauthorized();

        var orderSpec = new OrderWithItemsByIdSpec(request.OrderId);
        var order = (await orderRepo.ListAsync(orderSpec)).FirstOrDefault();
        if (order == null || order.BuyerId != request.BuyerId)
            return Results.NotFound(new { error = "Order not found." });

        if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
            return Results.Conflict(new { error = "Order is not awaiting payment." });

        var paymentRecordSpec = new PaymentRecordByOrderIdSpec(request.OrderId);
        var paymentRecord = (await paymentRepo.ListAsync(paymentRecordSpec)).FirstOrDefault();
        if (paymentRecord == null)
            return Results.Problem("Payment record not found.", statusCode: 500);

        decimal total = order.Total();
        string currency = settings.Currency;

        try
        {
            PayPalAuthResult authResult;

            if (request.PaymentMethodId.HasValue)
            {
                var savedCard = await savedCardRepo.GetByIdAsync(request.PaymentMethodId.Value);
                if (savedCard == null || savedCard.BuyerId != request.BuyerId || savedCard.IsDeleted)
                    return Results.BadRequest(new { error = "Payment method not found or not accessible." });

                authResult = await payPalService.AuthorizeWithVaultedCardAsync(
                    total, currency, paymentRecord.IdempotencyBase, savedCard.VaultTokenId);
            }
            else if (request.Card != null)
            {
                var cardSource = new PayPalCardSource(
                    request.Card.Number,
                    request.Card.Expiry,
                    request.Card.SecurityCode,
                    request.Card.Name,
                    request.Card.AddressLine1,
                    request.Card.City,
                    request.Card.State,
                    request.Card.CountryCode ?? "US",
                    request.Card.PostalCode);

                authResult = await payPalService.AuthorizeWithCardAsync(
                    total, currency, paymentRecord.IdempotencyBase, cardSource);
            }
            else
            {
                return Results.BadRequest(new { error = "Provide either card details or a paymentMethodId." });
            }

            paymentRecord.SetPayPalOrderId(authResult.PayPalOrderId);
            paymentRecord.SetAuthorized(authResult.AuthorizationId);
            await paymentRepo.UpdateAsync(paymentRecord);

            order.SetPaymentStatus(OrderPaymentStatus.Authorized);
            await orderRepo.UpdateAsync(order);

            return Results.Ok(new { message = "Payment authorized.", authorizationId = authResult.AuthorizationId });
        }
        catch (PayPalProviderException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.Problem("PayPal returned an unreadable response.", statusCode: 502);
        }
    }
}
