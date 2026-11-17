using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .Produces<PaymentOperationResult>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var card = request.Card is null
            ? null
            : new CardDetails(
                request.Card.Number,
                request.Card.ExpirationMonth,
                request.Card.ExpirationYear,
                request.Card.SecurityCode,
                request.Card.Name,
                request.Card.BillingAddress is null
                    ? null
                    : new BillingAddressDetails(
                        request.Card.BillingAddress.Street,
                        request.Card.BillingAddress.City,
                        request.Card.BillingAddress.State,
                        request.Card.BillingAddress.Country,
                        request.Card.BillingAddress.ZipCode));

        var result = await paymentService.PayAsync(
            request.OrderId,
            buyerId,
            card,
            request.PaymentMethodId,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);

        return Results.Ok(result);
    }
}
