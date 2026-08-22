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
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, IPaymentMethodService paymentMethodService, ClaimsPrincipal user) =>
            {
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Card is null)
        {
            throw new ApplicationCore.Exceptions.CheckoutException("Card details are required.", 400);
        }

        var method = await paymentMethodService.SaveAsync(request.BuyerId, request.Card.ToSource(), default);
        var body = PaymentMethodMapper.MapCreated(method);
        return Results.Created($"api/payment-methods/{method.Id}", body);
    }
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentMethodService paymentMethodService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest(user.Identity?.Name ?? string.Empty), paymentMethodService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentMethodService paymentMethodService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var methods = await paymentMethodService.ListAsync(request.BuyerId, default);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = methods.Select(PaymentMethodMapper.Map).ToList()
        });
    }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, IPaymentMethodService paymentMethodService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(
                    new DeletePaymentMethodRequest(user.Identity?.Name ?? string.Empty, paymentMethodId),
                    paymentMethodService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        await paymentMethodService.DeleteAsync(request.BuyerId, request.PaymentMethodId, default);
        return Results.NoContent();
    }
}

public static class PaymentMethodMapper
{
    public static PaymentMethodResponse Map(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        LastDigits = method.Last4,
        Brand = method.Brand,
        Expiry = method.Expiry
    };

    public static CreatePaymentMethodResponse MapCreated(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        LastDigits = method.Last4,
        Brand = method.Brand,
        Expiry = method.Expiry
    };
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public CardDetailsDto? Card { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

public class ListPaymentMethodsRequest : BaseRequest
{
    public string BuyerId { get; }

    public ListPaymentMethodsRequest(string buyerId)
    {
        BuyerId = buyerId;
    }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public string BuyerId { get; }
    public int PaymentMethodId { get; }

    public DeletePaymentMethodRequest(string buyerId, int paymentMethodId)
    {
        BuyerId = buyerId;
        PaymentMethodId = paymentMethodId;
    }
}
