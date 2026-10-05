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
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>How a saved card is shown back: enough to recognise it, never the card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Type { get; set; } = "card";
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        Alias = method.Alias,
        Description = $"{method.Brand ?? "Card"} ending in {method.Last4}{(method.Expiry is null ? "" : $", expires {method.Expiry}")}",
        CreatedAt = method.CreatedAt,
    };
}

public class SavePaymentMethodRequest : BaseRequest
{
    public CardInput? Card { get; set; }
    public string? Alias { get; set; }
    /// <summary>Optional: repeating a save under the same key returns the card saved the first time.</summary>
    public string? IdempotencyKey { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }

    public override string ToString() => "Save card [card details redacted]";
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SavePaymentMethodResponse()
    {
    }

    public int PaymentMethodId { get; set; }
    public bool AlreadySaved { get; set; }
    public PaymentMethodDto? PaymentMethod { get; set; }
}

/// <summary>Saves a card for the signed-in shopper (vaulted at PayPal; only a token is kept here).</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, SavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, SavedCardService service) =>
            {
                request.BuyerId = user.BuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, SavedCardService service)
    {
        if (request.BuyerId is null)
            return Results.Unauthorized();
        if (request.Card is null)
            throw new ApplicationCore.Exceptions.PaymentValidationException("card is required.");

        var result = await service.SaveAsync(request.BuyerId, request.Card.ToCardDetails(), request.Alias?.Trim(), request.IdempotencyKey?.Trim(), CancellationToken.None);
        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = result.PaymentMethod.Id,
            AlreadySaved = result.AlreadySaved,
            PaymentMethod = PaymentMethodDto.From(result.PaymentMethod),
        };
        return result.AlreadySaved
            ? Results.Ok(response)
            : Results.Created($"api/payment-methods", response);
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
    public string? BuyerId { get; set; }
    public CancellationToken CancellationToken { get; set; }
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

/// <summary>The signed-in shopper's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, SavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, SavedCardService service, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = user.BuyerId(), CancellationToken = cancellationToken }, service);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, SavedCardService service)
    {
        if (request.BuyerId is null)
            return Results.Unauthorized();

        var methods = await service.ListAsync(request.BuyerId, request.CancellationToken);
        return Results.Ok(new ListPaymentMethodsResponse(request.CorrelationId())
        {
            PaymentMethods = methods.Select(PaymentMethodDto.From).ToList(),
        });
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
    public string? BuyerId { get; set; }
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
    public bool Removed { get; set; }
    /// <summary>The card is gone for the shopper; PayPal's copy is still being deleted (repeat the DELETE to retry).</summary>
    public bool ProviderDeletionPending { get; set; }
}

/// <summary>Removes one of the signed-in shopper's saved cards; it can no longer be listed or used to pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, SavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, SavedCardService service) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId, BuyerId = user.BuyerId() }, service);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, SavedCardService service)
    {
        if (request.BuyerId is null)
            return Results.Unauthorized();

        var result = await service.RemoveAsync(request.BuyerId, request.PaymentMethodId, CancellationToken.None);
        return Results.Ok(new DeletePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = result.PaymentMethod.Id,
            Removed = true,
            ProviderDeletionPending = result.ProviderDeletionPending,
        });
    }
}
