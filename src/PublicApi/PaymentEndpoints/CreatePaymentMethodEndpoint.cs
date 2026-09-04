using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public PayPalCardRequest Card { get; set; } = new();
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = "";
    public string Last4 { get; set; } = "";
    public string Expiry { get; set; } = "";
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = "";
    public string Last4 { get; set; } = "";
    public string Expiry { get; set; } = "";
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class ListPaymentMethodsRequest : BaseRequest
{
}

/// <summary>
/// Saves a card for the signed-in shopper via PayPal's vault. Only non-sensitive
/// display details (brand, last four digits) are returned and stored.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize] async (CreatePaymentMethodRequest request, IHttpContextAccessor httpContextAccessor, IPaymentService paymentService) =>
                await HandleAsync(request, httpContextAccessor, paymentService))
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request) => throw new NotSupportedException();

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IHttpContextAccessor httpContextAccessor, IPaymentService paymentService)
    {
        var buyerId = httpContextAccessor.HttpContext.User.RequireBuyerId();

        var method = await paymentService.SavePaymentMethodAsync(buyerId, request.Card.ToCardPayment()
            ?? throw new OrderPaymentException("missing_card", "Card details are required."));

        return Results.Ok(new CreatePaymentMethodResponse
        {
            PaymentMethodId = method.Id,
            Brand = method.Brand,
            Last4 = method.Last4,
            Expiry = method.Expiry,
            CardholderName = method.CardholderName,
            CreatedAt = method.CreatedAt
        });
    }
}

/// <summary>
/// Lists the signed-in shopper's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize] async (IHttpContextAccessor httpContextAccessor, IPaymentService paymentService) =>
                await HandleAsync(httpContextAccessor, paymentService))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ListPaymentMethodsRequest request) => throw new NotSupportedException();

    public async Task<IResult> HandleAsync(IHttpContextAccessor httpContextAccessor, IPaymentService paymentService)
    {
        var buyerId = httpContextAccessor.HttpContext.User.RequireBuyerId();

        var methods = await paymentService.ListPaymentMethodsAsync(buyerId);

        var response = new ListPaymentMethodsResponse();
        foreach (var method in methods)
        {
            response.PaymentMethods.Add(new PaymentMethodDto
            {
                PaymentMethodId = method.Id,
                Brand = method.Brand,
                Last4 = method.Last4,
                Expiry = method.Expiry,
                CardholderName = method.CardholderName,
                CreatedAt = method.CreatedAt
            });
        }
        return Results.Ok(response);
    }
}
