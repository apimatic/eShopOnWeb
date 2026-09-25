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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class SavePaymentMethodRequest
{
    public CardRequestModel Card { get; set; } = new();

    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken CancellationToken { get; set; }
}

/// <summary>
/// POST /api/payment-methods — saves a card for the signed-in shopper. Returns
/// <c>paymentMethodId</c> as a top-level field and a safe description; never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService service, HttpContext ctx) =>
            {
                request.BuyerId = CallerIdentity.BuyerId(user);
                request.CancellationToken = ctx.RequestAborted;
                return await HandleAsync(request, service);
            })
            .Produces<SavedCardDto>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentMethodService service)
    {
        var saved = await service.SaveCardAsync(request.BuyerId, request.Card.ToDomain(), request.CancellationToken);
        return Results.Created($"api/payment-methods/{saved.Id}", PaymentMappings.ToDto(saved));
    }
}

public class ListPaymentMethodsRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public CancellationToken CancellationToken { get; set; }
}

public record ListPaymentMethodsResponse(IReadOnlyList<SavedCardDto> PaymentMethods);

/// <summary>GET /api/payment-methods — the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentMethodService service, HttpContext ctx) =>
                await HandleAsync(new ListPaymentMethodsRequest
                {
                    BuyerId = CallerIdentity.BuyerId(user),
                    CancellationToken = ctx.RequestAborted
                }, service))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentMethodService service)
    {
        var cards = await service.GetCardsAsync(request.BuyerId, request.CancellationToken);
        return Results.Ok(new ListPaymentMethodsResponse(cards.Select(PaymentMappings.ToDto).ToList()));
    }
}

public class DeletePaymentMethodRequest
{
    public int PaymentMethodId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public CancellationToken CancellationToken { get; set; }
}

/// <summary>
/// DELETE /api/payment-methods/{paymentMethodId} — removes the caller's saved card. Afterwards it
/// no longer appears among their cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentMethodService service, HttpContext ctx) =>
                await HandleAsync(new DeletePaymentMethodRequest
                {
                    PaymentMethodId = paymentMethodId,
                    BuyerId = CallerIdentity.BuyerId(user),
                    CancellationToken = ctx.RequestAborted
                }, service))
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService service)
    {
        var deleted = await service.DeleteCardAsync(request.PaymentMethodId, request.BuyerId, request.CancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
