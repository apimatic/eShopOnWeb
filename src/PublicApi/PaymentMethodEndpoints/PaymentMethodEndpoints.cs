using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CardDetailsRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, HttpContext http, IOrderCheckoutService checkout) =>
            {
                var saved = await checkout.SavePaymentMethodAsync(http.User.GetBuyerId(), request.Card.ToDetails());
                var body = PaymentMethodResponse.From(saved);
                return Results.Created($"api/payment-methods/{body.PaymentMethodId}", body);
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CardDetailsRequest request, IOrderCheckoutService checkout) =>
        Task.FromResult(Results.BadRequest());
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, IOrderCheckoutService checkout) =>
            {
                var methods = await checkout.ListPaymentMethodsAsync(http.User.GetBuyerId());
                return Results.Ok(new PaymentMethodListResponse
                {
                    PaymentMethods = methods.Select(PaymentMethodResponse.From).ToList()
                });
            })
            .Produces<PaymentMethodListResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderCheckoutService checkout) =>
        Task.FromResult(Results.BadRequest());
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, HttpContext http, IOrderCheckoutService checkout) =>
            {
                await checkout.DeletePaymentMethodAsync(http.User.GetBuyerId(), paymentMethodId);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int request, IOrderCheckoutService checkout) =>
        Task.FromResult(Results.BadRequest());
}

public class SavePaymentMethodRequest
{
    public CardDetailsRequest Card { get; set; } = new();
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }

    public static PaymentMethodResponse From(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        LastDigits = method.LastDigits,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };
}

public class PaymentMethodListResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}
