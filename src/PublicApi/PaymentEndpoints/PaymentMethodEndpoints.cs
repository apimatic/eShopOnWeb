using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>A saved card as shown to the shopper — safe details only, never the full number.</summary>
public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedCardDto From(SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        Last4 = card.Last4,
        Expiry = card.Expiry,
        CardholderName = card.CardholderName,
        CreatedAt = card.CreatedAt
    };
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsRequest Card { get; set; } = new();
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    /// <summary>Top-level identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
}

/// <summary>POST /api/payment-methods — save (vault) a card for the signed-in shopper.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, HttpContext>
{
    private readonly ISavedCardService _savedCards;

    public CreatePaymentMethodEndpoint(ISavedCardService savedCards) => _savedCards = savedCards;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, HttpContext http)
    {
        if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
        {
            return Results.BadRequest("Card details are required to save a payment method.");
        }

        var saved = await _savedCards.SaveCardAsync(http.BuyerId(), request.Card.ToCardDetails(), http.RequestAborted);
        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            Brand = saved.Brand,
            Last4 = saved.Last4,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

/// <summary>GET /api/payment-methods — the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly ISavedCardService _savedCards;

    public ListPaymentMethodsEndpoint(ISavedCardService savedCards) => _savedCards = savedCards;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, CancellationToken ct) => await HandleAsync(http))
            .Produces<List<SavedCardDto>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var cards = await _savedCards.ListAsync(http.BuyerId(), http.RequestAborted);
        return Results.Ok(cards.Select(SavedCardDto.From).ToList());
    }
}

/// <summary>DELETE /api/payment-methods/{paymentMethodId} — remove one of the caller's saved cards.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly ISavedCardService _savedCards;

    public DeletePaymentMethodEndpoint(ISavedCardService savedCards) => _savedCards = savedCards;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, CancellationToken ct) => await HandleAsync(http))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        await _savedCards.DeleteAsync(http.BuyerId(), http.RouteInt("paymentMethodId"), http.RequestAborted);
        return Results.NoContent();
    }
}
