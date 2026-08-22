using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, ClaimsPrincipal user, ICheckoutService checkout) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, checkout);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ICheckoutService checkout)
    {
        var card = OrderDtoMapper.ToCard(request.ResolveCard());
        if (card is null)
        {
            throw new CheckoutException(400, "Card details are required to save a payment method.");
        }

        var saved = await checkout.SaveCardAsync(request.BuyerId, card);
        var response = new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = PaymentMethodDto.From(saved)
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, ICheckoutService checkout) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = user.GetBuyerId() }, checkout);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ICheckoutService checkout)
    {
        var cards = await checkout.ListSavedCardsAsync(request.BuyerId);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(PaymentMethodDto.From).ToList()
        });
    }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ClaimsPrincipal user, ICheckoutService checkout) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest
                {
                    BuyerId = user.GetBuyerId(),
                    PaymentMethodId = paymentMethodId
                }, checkout);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ICheckoutService checkout)
    {
        await checkout.DeleteSavedCardAsync(request.BuyerId, request.PaymentMethodId);
        return Results.NoContent();
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public CardRequest? Card { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }

    public CardRequest? ResolveCard() => Card ?? (string.IsNullOrWhiteSpace(Number)
        ? null
        : new CardRequest
        {
            Number = Number,
            Expiry = Expiry,
            SecurityCode = SecurityCode,
            Name = Name,
            BillingAddress = BillingAddress
        });
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public int PaymentMethodId { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }

    public static PaymentMethodDto From(SavedPaymentMethod saved) => new()
    {
        PaymentMethodId = saved.Id,
        Brand = saved.Brand,
        LastDigits = saved.LastDigits,
        Expiry = saved.Expiry,
        CardholderName = saved.CardholderName
    };
}
