using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<Order>, IRepository<PaymentReference>, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext httpContext, IRepository<Order> orderRepo,
             IRepository<PaymentReference> paymentRepo, IPaymentService paymentService) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                return await HandleAsync(request, orderRepo, paymentRepo, paymentService, orderId, userId);
            })
            .Produces<PayOrderResponse>()
            .WithName("PayOrder")
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> orderRepo,
        IRepository<PaymentReference> paymentRepo, IPaymentService paymentService, int orderId, string userId)
    {
        var order = await orderRepo.GetByIdAsync(orderId);
        if (order == null)
            return Results.NotFound("Order not found");

        if (order.BuyerId != userId)
            return Results.Forbid();

        var existingPayment = (await paymentRepo.ListAsync(p => p.OrderId == orderId)).FirstOrDefault();
        if (existingPayment != null)
            return Results.BadRequest("Order already has a payment reference");

        var paymentDetails = new PaymentDetails
        {
            SavedPaymentMethodId = request.SavedPaymentMethodId,
            CardDetails = request.CardDetails != null ? new CardDetails
            {
                CardNumber = request.CardDetails.CardNumber,
                Expiry = request.CardDetails.Expiry,
                Cvv = request.CardDetails.Cvv,
                CardholderName = request.CardDetails.CardholderName
            } : null
        };

        var result = await paymentService.AuthorizePaymentAsync(order, paymentDetails, Guid.NewGuid().ToString());
        if (!result.Success)
            return Results.BadRequest(new { error = result.ErrorMessage });

        var paymentRef = new PaymentReference(orderId);
        paymentRef.SetAuthorization(result.OrderId!, result.AuthorizationId!, order.Total(), "USD");
        await paymentRepo.AddAsync(paymentRef);
        await paymentRepo.SaveChangesAsync();

        return Results.Ok(new PayOrderResponse { AuthorizationId = result.AuthorizationId });
    }
}

public record PayOrderRequest(string? SavedPaymentMethodId, CardDetailsRequest? CardDetails);
public record CardDetailsRequest(string CardNumber, string Expiry, string Cvv, string CardholderName);
public record PayOrderResponse
{
    public string? AuthorizationId { get; set; }
}
