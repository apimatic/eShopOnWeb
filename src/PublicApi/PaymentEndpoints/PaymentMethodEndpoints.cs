using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

/// <summary>POST /api/payment-methods — save a card for the signed-in shopper.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodEndpoint.Command, IOrderPaymentService>
{
    public record Command(string BuyerId, SavePaymentMethodRequest Body, CancellationToken CancellationToken);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IOrderPaymentService service, HttpContext http) =>
            {
                var buyerId = PaymentEndpointHelpers.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(new Command(buyerId, request, http.RequestAborted), service);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags(PaymentEndpointHelpers.Tag);
    }

    public async Task<IResult> HandleAsync(Command command, IOrderPaymentService service)
    {
        var saved = await service.SaveCardAsync(command.BuyerId, command.Body.Card.ToCardDetails(), command.CancellationToken);
        var response = new SavePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            Brand = saved.Brand,
            LastDigits = saved.LastDigits,
            Expiry = saved.Expiry,
            CardHolderName = saved.CardHolderName
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

/// <summary>GET /api/payment-methods — the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsEndpoint.Command, IOrderPaymentService>
{
    public record Command(string BuyerId, CancellationToken CancellationToken);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service, HttpContext http) =>
            {
                var buyerId = PaymentEndpointHelpers.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(new Command(buyerId, http.RequestAborted), service);
            })
            .Produces<List<PaymentMethodDto>>()
            .WithTags(PaymentEndpointHelpers.Tag);
    }

    public async Task<IResult> HandleAsync(Command command, IOrderPaymentService service)
    {
        var cards = await service.GetSavedCardsAsync(command.BuyerId, command.CancellationToken);
        return Results.Ok(cards.Select(PaymentMethodDto.From).ToList());
    }
}

/// <summary>DELETE /api/payment-methods/{paymentMethodId} — remove a saved card.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodEndpoint.Command, IOrderPaymentService>
{
    public record Command(string BuyerId, int PaymentMethodId, CancellationToken CancellationToken);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IOrderPaymentService service, HttpContext http) =>
            {
                var buyerId = PaymentEndpointHelpers.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(new Command(buyerId, paymentMethodId, http.RequestAborted), service);
            })
            .WithTags(PaymentEndpointHelpers.Tag);
    }

    public async Task<IResult> HandleAsync(Command command, IOrderPaymentService service)
    {
        await service.DeleteSavedCardAsync(command.BuyerId, command.PaymentMethodId, command.CancellationToken);
        return Results.NoContent();
    }
}
