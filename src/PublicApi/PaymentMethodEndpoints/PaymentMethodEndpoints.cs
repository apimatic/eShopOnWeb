using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
            async (CreatePaymentMethodRequest request, ICheckoutService checkout, ClaimsPrincipal user) =>
            {
                request.BuyerId = ApiUser.GetBuyerId(user);
                return await HandleAsync(request, checkout);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ICheckoutService checkout)
    {
        var saved = await checkout.SaveCardAsync(request.BuyerId!, new CardPaymentDetails
        {
            Name = request.Name,
            Number = request.Number,
            Expiry = request.Expiry,
            SecurityCode = request.SecurityCode,
            BillingAddress = request.BillingAddress is null ? null : new CardBillingAddress
            {
                AddressLine1 = request.BillingAddress.AddressLine1,
                AddressLine2 = request.BillingAddress.AddressLine2,
                AdminArea2 = request.BillingAddress.AdminArea2,
                AdminArea1 = request.BillingAddress.AdminArea1,
                PostalCode = request.BillingAddress.PostalCode,
                CountryCode = request.BillingAddress.CountryCode
            }
        });

        var mapped = PaymentResponseMapper.MapSavedCard(saved);
        return Results.Created($"api/payment-methods/{mapped.PaymentMethodId}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = mapped.PaymentMethodId,
            Brand = mapped.Brand,
            LastDigits = mapped.LastDigits,
            Expiry = mapped.Expiry,
            CardholderName = mapped.CardholderName
        });
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string? BuyerId { get; set; }
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public PayCardAddressRequest? BillingAddress { get; set; }
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ICheckoutService checkout, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = ApiUser.GetBuyerId(user) }, checkout);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ICheckoutService checkout)
    {
        var cards = await checkout.ListSavedCardsAsync(request.BuyerId!);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(PaymentResponseMapper.MapSavedCard).ToList()
        });
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
    public string? BuyerId { get; set; }
}

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ICheckoutService checkout, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest
                {
                    PaymentMethodId = paymentMethodId,
                    BuyerId = ApiUser.GetBuyerId(user)
                }, checkout);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ICheckoutService checkout)
    {
        await checkout.DeleteSavedCardAsync(request.BuyerId!, request.PaymentMethodId);
        return Results.NoContent();
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
    public string? BuyerId { get; set; }
}
