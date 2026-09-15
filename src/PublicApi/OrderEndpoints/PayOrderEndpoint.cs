using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRouteRequest, ClaimsPrincipal>
{
    private readonly IOrderPaymentService _paymentService;
    private readonly IPaymentCurrencyAccessor _currency;

    public PayOrderEndpoint(IOrderPaymentService paymentService, IPaymentCurrencyAccessor currency)
    {
        _paymentService = paymentService;
        _currency = currency;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user) =>
                await HandleAsync(new PayOrderRouteRequest(orderId, request), user))
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRouteRequest routeRequest, ClaimsPrincipal user)
    {
        var buyerId = user.RequireUserName();
        var request = routeRequest.Body ?? new PayOrderRequest();
        var hasCard = request.Card is not null && !string.IsNullOrWhiteSpace(request.Card.Number);
        var hasSaved = request.PaymentMethodId.HasValue;

        if (hasCard == hasSaved)
        {
            throw new PaymentException(400, "Provide either card details or paymentMethodId, not both.");
        }

        var order = hasSaved
            ? await _paymentService.PayWithSavedCardAsync(routeRequest.OrderId, buyerId, request.PaymentMethodId!.Value)
            : await _paymentService.PayWithCardAsync(routeRequest.OrderId, buyerId, CardMapper.ToCardPaymentSource(request.Card!));

        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.FromOrder(order, _currency.Currency)
        });
    }
}

public record PayOrderRouteRequest(int OrderId, PayOrderRequest Body);

internal static class CardMapper
{
    public static CardPaymentSource ToCardPaymentSource(PayOrderCardRequest card)
    {
        var number = Regex.Replace(card.Number ?? string.Empty, @"\s+", string.Empty);
        if (string.IsNullOrWhiteSpace(card.Name)
            || string.IsNullOrWhiteSpace(number)
            || string.IsNullOrWhiteSpace(card.Expiry)
            || string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new PaymentException(400, "Card name, number, expiry, and security code are required.");
        }

        if (!Regex.IsMatch(card.Expiry, @"^\d{4}-\d{2}$"))
        {
            throw new PaymentException(400, "Card expiry must be in YYYY-MM format.");
        }

        var billing = card.BillingAddress ?? new PayOrderBillingAddressRequest();
        if (string.IsNullOrWhiteSpace(billing.CountryCode))
        {
            billing.CountryCode = "US";
        }

        return new CardPaymentSource
        {
            Name = card.Name.Trim(),
            Number = number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = new CardBillingAddress
            {
                CountryCode = billing.CountryCode,
                AddressLine1 = billing.AddressLine1,
                AddressLine2 = billing.AddressLine2,
                AdminArea2 = billing.AdminArea2,
                AdminArea1 = billing.AdminArea1,
                PostalCode = billing.PostalCode
            }
        };
    }
}
