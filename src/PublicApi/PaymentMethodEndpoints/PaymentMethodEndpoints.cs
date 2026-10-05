using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>What a shopper sees of a saved card — enough to recognise it, never the card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Type { get; set; } = "card";
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(SavedPaymentMethod m) => new()
    {
        PaymentMethodId = m.Id,
        Brand = m.Brand,
        LastDigits = m.LastDigits,
        Expiry = m.Expiry,
        CardholderName = m.CardholderName,
        CreatedAt = m.CreatedAt
    };
}

public class SavePaymentMethodRequest : BaseRequest
{
    public CardInputDto? Card { get; set; }

    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken RequestAborted { get; set; }

    public override string ToString() => "SavePaymentMethodRequest([card details redacted])";
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SavePaymentMethodResponse()
    {
    }

    [JsonPropertyOrder(-1)]
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto? PaymentMethod { get; set; }
}

/// <summary>Saves a card for the signed-in shopper in PayPal's vault.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, PaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, PaymentMethodService service, CancellationToken ct) =>
            {
                request.BuyerId = PaymentEndpointUser.BuyerId(user);
                request.RequestAborted = ct;
                return await HandleAsync(request, service);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, PaymentMethodService service)
    {
        if (request.Card is null)
            throw new PaymentRequestException(PaymentErrorKind.Validation, "card_required", "Card details are required.");

        var method = await service.SaveAsync(request.BuyerId, request.Card.ToCardDetails(), request.RequestAborted);
        return Results.Created($"api/payment-methods/{method.Id}", new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = method.Id,
            PaymentMethod = PaymentMethodDto.From(method)
        });
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken RequestAborted { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListPaymentMethodsResponse()
    {
    }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

/// <summary>The caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, PaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, PaymentMethodService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = PaymentEndpointUser.BuyerId(user), RequestAborted = ct }, service);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, PaymentMethodService service)
    {
        var methods = await service.ListAsync(request.BuyerId, request.RequestAborted);
        return Results.Ok(new ListPaymentMethodsResponse(request.CorrelationId())
        {
            PaymentMethods = methods.Select(PaymentMethodDto.From).ToList()
        });
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken RequestAborted { get; set; }
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId)
    {
    }

    public DeletePaymentMethodResponse()
    {
    }

    public int PaymentMethodId { get; set; }
    public string Status { get; set; } = "Deleted";

    /// <summary>False while PayPal has not yet confirmed the vault token is gone (the card is already unusable).</summary>
    public bool RemovedFromPayPal { get; set; }
}

/// <summary>Removes one of the caller's saved cards: it disappears from the list and can no longer pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, PaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, PaymentMethodService service, CancellationToken ct) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest
                {
                    PaymentMethodId = paymentMethodId,
                    BuyerId = PaymentEndpointUser.BuyerId(user),
                    RequestAborted = ct
                }, service);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, PaymentMethodService service)
    {
        var method = await service.DeleteAsync(request.BuyerId, request.PaymentMethodId, request.RequestAborted);
        return Results.Ok(new DeletePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = method.Id,
            RemovedFromPayPal = method.VaultTokenRemoved
        });
    }
}
