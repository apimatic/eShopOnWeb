using System;
using System.Text.Json.Serialization;
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

public class PayOrderRequest : BaseRequest
{
    public int? PaymentMethodId { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public CardRequest? Card { get; set; }

    public string? Number { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardAddressRequest? Address { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    public PayOrderCommand ToCommand()
    {
        var card = Card ?? (string.IsNullOrWhiteSpace(Number)
            ? null
            : new CardRequest
            {
                Number = Number,
                ExpirationMonth = ExpirationMonth ?? 0,
                ExpirationYear = ExpirationYear ?? 0,
                SecurityCode = SecurityCode,
                Name = Name,
                Address = Address
            });

        return new PayOrderCommand
        {
            PaymentMethodId = PaymentMethodId ?? SavedPaymentMethodId,
            Card = card is null ? null : new CardDetails
            {
                Number = card.Number,
                ExpirationMonth = card.ExpirationMonth,
                ExpirationYear = card.ExpirationYear,
                SecurityCode = card.SecurityCode,
                Name = card.Name,
                Address = card.Address is null ? null : new CardBillingAddress
                {
                    Line1 = card.Address.Line1,
                    Line2 = card.Address.Line2,
                    City = card.Address.City,
                    Region = card.Address.Region,
                    PostalCode = card.Address.PostalCode,
                    Country = card.Address.Country
                }
            }
        };
    }
}

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext httpContext, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = httpContext.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, paymentService);
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        var order = await paymentService.PayAsync(request.BuyerId,
            request.OrderId,
            request.ToCommand());

        return Results.Ok(OrderDtoMapper.ToActionResponse(order, request.CorrelationId()));
    }
}
