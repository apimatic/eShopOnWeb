using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
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
            (int orderId, PayOrderRequest request, IOrderPaymentService orders) =>
            {
                return await HandleAsync(orderId, request, orders);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orders)
    {
        throw new System.InvalidOperationException("Order id is required.");
    }

    private async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, IOrderPaymentService orders)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new System.InvalidOperationException("HTTP context is not available.");
        var buyerId = httpContext.User.GetBuyerId();

        CardPaymentDetails? card = null;
        if (request.Card is not null)
        {
            card = new CardPaymentDetails
            {
                Number = request.Card.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.SecurityCode,
                Name = request.Card.Name,
                BillingAddress = request.Card.BillingAddress is null
                    ? null
                    : new CardBillingAddress
                    {
                        AddressLine1 = request.Card.BillingAddress.AddressLine1,
                        AddressLine2 = request.Card.BillingAddress.AddressLine2,
                        AdminArea2 = request.Card.BillingAddress.AdminArea2,
                        AdminArea1 = request.Card.BillingAddress.AdminArea1,
                        PostalCode = request.Card.BillingAddress.PostalCode,
                        CountryCode = request.Card.BillingAddress.CountryCode
                    }
            };
        }

        var order = await orders.PayAsync(
            buyerId,
            orderId,
            card,
            request.PaymentMethodId,
            httpContext.RequestAborted);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDto.From(order)
        };

        return Results.Ok(response);
    }
}
