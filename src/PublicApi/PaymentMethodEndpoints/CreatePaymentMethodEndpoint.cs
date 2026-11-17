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
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public string? Number { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? Alias { get; set; }
    public CardAddressRequest? Address { get; set; }
    public CardRequest? Card { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    public CardDetails ToCardDetails()
    {
        var card = Card ?? new CardRequest
        {
            Number = Number ?? string.Empty,
            ExpirationMonth = ExpirationMonth ?? 0,
            ExpirationYear = ExpirationYear ?? 0,
            SecurityCode = SecurityCode,
            Name = Name,
            Address = Address
        };

        return new CardDetails
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
        };
    }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) {}
    public CreatePaymentMethodResponse() {}

    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, HttpContext httpContext, IPaymentService paymentService) =>
            {
                request.BuyerId = httpContext.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, paymentService);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentService paymentService)
    {
        var paymentMethod = await paymentService.SavePaymentMethodAsync(request.BuyerId,
            request.ToCardDetails(),
            request.Alias);

        return Results.Created($"api/payment-methods/{paymentMethod.Id}",
            new CreatePaymentMethodResponse(request.CorrelationId())
            {
                PaymentMethodId = paymentMethod.Id,
                PaymentMethod = PaymentMethodDtoMapper.ToDto(paymentMethod)
            });
    }
}
