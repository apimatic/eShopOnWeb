using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, HttpContext httpContext, IPaymentMethodService paymentMethodService) =>
            {
                request.BuyerId = httpContext.User.Identity?.Name
                    ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
                    ?? string.Empty;
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        var card = new CardPaymentDetails
        {
            Number = request.Card?.Number ?? string.Empty,
            Expiry = request.Card?.Expiry ?? string.Empty,
            SecurityCode = request.Card?.SecurityCode ?? string.Empty,
            Name = request.Card?.Name,
            BillingAddress = request.Card?.BillingAddress is null
                ? null
                : new CardBillingAddress
                {
                    AddressLine1 = request.Card.BillingAddress.Street ?? request.Card.BillingAddress.AddressLine1,
                    AddressLine2 = request.Card.BillingAddress.AddressLine2,
                    AdminArea2 = request.Card.BillingAddress.City ?? request.Card.BillingAddress.AdminArea2,
                    AdminArea1 = request.Card.BillingAddress.State ?? request.Card.BillingAddress.AdminArea1,
                    PostalCode = request.Card.BillingAddress.ZipCode ?? request.Card.BillingAddress.PostalCode,
                    CountryCode = request.Card.BillingAddress.Country ?? request.Card.BillingAddress.CountryCode ?? "US"
                }
        };

        var method = await paymentMethodService.SaveCardAsync(request.BuyerId, card);
        var response = new CreatePaymentMethodResponse
        {
            PaymentMethodId = method.Id,
            PaymentMethod = PaymentMethodDto.From(method)
        };
        return Results.Created($"api/payment-methods/{method.Id}", response);
    }
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IPaymentMethodService paymentMethodService) =>
            {
                var buyerId = httpContext.User.Identity?.Name
                    ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
                    ?? string.Empty;
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = buyerId }, paymentMethodService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentMethodService paymentMethodService)
    {
        var methods = await paymentMethodService.ListAsync(request.BuyerId);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = methods.Select(PaymentMethodDto.From).ToList()
        });
    }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, HttpContext httpContext, IPaymentMethodService paymentMethodService) =>
            {
                var buyerId = httpContext.User.Identity?.Name
                    ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
                    ?? string.Empty;
                return await HandleAsync(new DeletePaymentMethodRequest
                {
                    PaymentMethodId = paymentMethodId,
                    BuyerId = buyerId
                }, paymentMethodService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        await paymentMethodService.DeleteAsync(request.BuyerId, request.PaymentMethodId);
        return Results.NoContent();
    }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Alias { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }

    public static PaymentMethodDto From(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Alias = method.Alias,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry
    };
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardRequest? Card { get; set; }
    internal string BuyerId { get; set; } = string.Empty;
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}
