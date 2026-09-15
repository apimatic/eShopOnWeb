using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly IOptions<PayPalSettings> _payPalSettings;

    public PayOrderEndpoint(IOptions<PayPalSettings> payPalSettings)
    {
        _payPalSettings = payPalSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, HttpContext http, IOrderPaymentService payments) =>
            {
                request.OrderId = orderId;
                request.BuyerId = http.RequireBuyerId();
                return await HandleAsync(request, payments);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService payments)
    {
        var command = new PayOrderCommand
        {
            PaymentMethodId = request.PaymentMethodId,
            Card = request.Card is null ? null : new CardPaymentInput
            {
                Name = request.Card.Name ?? string.Empty,
                Number = request.Card.Number ?? string.Empty,
                Expiry = request.Card.Expiry ?? string.Empty,
                SecurityCode = request.Card.SecurityCode ?? string.Empty,
                BillingAddress = new CardBillingAddressInput
                {
                    CountryCode = request.Card.BillingAddress?.CountryCode ?? string.Empty,
                    AddressLine1 = request.Card.BillingAddress?.AddressLine1,
                    AddressLine2 = request.Card.BillingAddress?.AddressLine2,
                    AdminArea2 = request.Card.BillingAddress?.AdminArea2,
                    AdminArea1 = request.Card.BillingAddress?.AdminArea1,
                    PostalCode = request.Card.BillingAddress?.PostalCode
                }
            }
        };

        var order = await payments.PayAsync(request.BuyerId, request.OrderId, command, default);
        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDto.From(order, _payPalSettings.Value.Currency)
        });
    }
}
