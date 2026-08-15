using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class SavePaymentMethodRequest
{
    public CardRequestDto Card { get; set; } = default!;
    public string? Alias { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = default!;
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = default!;
    public string Last4 { get; set; } = default!;
    public string? Expiry { get; set; }
    public string? Alias { get; set; }

    public static PaymentMethodDto From(PaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Brand = pm.Brand,
        Last4 = pm.Last4,
        Expiry = pm.Expiry,
        Alias = pm.Alias
    };
}

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

/// <summary>Saves a card for the signed-in shopper (vaulted at PayPal). Returns a safe descriptor.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService service) =>
            {
                request.BuyerId = ApiCaller.BuyerId(user);
                return await HandleAsync(request, service);
            })
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentMethodService service)
    {
        var pm = await service.SaveCardAsync(request.BuyerId, request.Card.ToCardDetails(), request.Alias);
        return Results.Created($"api/payment-methods/{pm.Id}", PaymentMethodDto.From(pm));
    }
}

/// <summary>Lists the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, string, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentMethodService service) =>
            {
                return await HandleAsync(ApiCaller.BuyerId(user), service);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(string buyerId, IPaymentMethodService service)
    {
        var cards = await service.GetCardsAsync(buyerId);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(PaymentMethodDto.From).ToList()
        });
    }
}

/// <summary>Removes one of the caller's saved cards; afterwards it can no longer be used to pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodEndpoint.Args, IPaymentMethodService>
{
    public record Args(string BuyerId, int PaymentMethodId);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentMethodService service) =>
            {
                return await HandleAsync(new Args(ApiCaller.BuyerId(user), paymentMethodId), service);
            })
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(Args args, IPaymentMethodService service)
    {
        await service.DeleteCardAsync(args.BuyerId, args.PaymentMethodId);
        return Results.NoContent();
    }
}
