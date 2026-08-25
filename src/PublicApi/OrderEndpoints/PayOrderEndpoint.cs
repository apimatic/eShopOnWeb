using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using AppAuthorizationResult = Microsoft.eShopWeb.ApplicationCore.Interfaces.AuthorizationResult;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record PayOrderRequest(
    int OrderId,
    string? SavedPaymentMethodId = null,
    string? CardNumber = null,
    int? ExpiryMonth = null,
    int? ExpiryYear = null,
    string? Cvv = null,
    string? CardholderName = null,
    string? CountryCode = null,
    string? Street = null,
    string? City = null,
    string? State = null,
    string? PostalCode = null
);

public record PayOrderResponse(string AuthorizationId, string Status, string? ExpiresAt);

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<Order>>
{
    private readonly IPayPalPaymentService _payPal;
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IPayPalPaymentService payPal, IConfiguration config, IHttpContextAccessor httpContextAccessor)
    {
        _payPal = payPal;
        _config = config;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IRepository<Order> orderRepo) =>
            {
                var mergedRequest = request with { OrderId = orderId };
                return await HandleAsync(mergedRequest, orderRepo);
            })
            .Produces<PayOrderResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> orderRepo)
    {
        var httpCtx = _httpContextAccessor.HttpContext;
        var ct = httpCtx?.RequestAborted ?? default;
        var user = httpCtx?.User;
        var buyerId = user?.FindFirstValue(ClaimTypes.Email)
                   ?? user?.FindFirstValue("sub")
                   ?? user?.Identity?.Name;

        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var spec = new OrderWithPaymentByIdSpec(request.OrderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec, ct);
        if (order == null) return Results.NotFound();
        if (order.BuyerId != buyerId) return Results.Forbid();
        if (order.PaymentStatus != PaymentStatus.PendingPayment)
            return Results.BadRequest($"Order is in state {order.PaymentStatus} and cannot be paid.");

        var currency = _config["PayPal:Currency"] ?? "USD";
        var idempotencyKey = $"pay-{order.Id}-{buyerId}";

        try
        {
            var ppOrderId = await _payPal.CreateOrderAsync(order.Total(), currency, $"create-{idempotencyKey}", ct);
            order.SetPayPalOrderId(ppOrderId);

            AppAuthorizationResult authResult;

            if (!string.IsNullOrWhiteSpace(request.SavedPaymentMethodId))
            {
                authResult = await _payPal.AuthorizeWithVaultTokenAsync(ppOrderId, request.SavedPaymentMethodId, idempotencyKey, ct);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.CardNumber) ||
                    request.ExpiryMonth == null || request.ExpiryYear == null ||
                    string.IsNullOrWhiteSpace(request.Cvv) ||
                    string.IsNullOrWhiteSpace(request.CardholderName) ||
                    string.IsNullOrWhiteSpace(request.CountryCode))
                {
                    return Results.BadRequest("Either savedPaymentMethodId or full card details are required.");
                }

                var card = new CardPaymentDetails(
                    request.CardNumber!, request.ExpiryMonth!.Value, request.ExpiryYear!.Value,
                    request.Cvv!, request.CardholderName!, request.CountryCode!,
                    request.Street, request.City, request.State, request.PostalCode);

                authResult = await _payPal.AuthorizeWithCardAsync(ppOrderId, card, idempotencyKey, ct);
            }

            order.Authorize(authResult.AuthorizationId, authResult.ExpiresAt);
            await orderRepo.UpdateAsync(order, ct);

            return Results.Ok(new PayOrderResponse(authResult.AuthorizationId, authResult.Status, authResult.ExpiresAt));
        }
        catch (PayPalPaymentException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
        }
    }
}
