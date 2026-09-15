using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    private readonly IHttpContextAccessor _http;

    public CreatePaymentMethodEndpoint(IHttpContextAccessor http)
    {
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedPaymentMethodService methods) =>
            {
                return await HandleAsync(request, methods);
            })
            .Produces<PaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService methods)
    {
        var buyerId = _http.HttpContext!.RequireBuyerId();
        var card = request.Card;
        var saved = await methods.SaveCardAsync(
            buyerId,
            new CardPaymentRequest(
                card.Number,
                card.Expiry,
                card.SecurityCode,
                card.Name,
                card.BillingAddress is null
                    ? null
                    : new ShippingAddressRequest(
                        card.BillingAddress.Street,
                        card.BillingAddress.City,
                        card.BillingAddress.State,
                        card.BillingAddress.Country,
                        card.BillingAddress.ZipCode)));

        return Results.Created($"api/payment-methods/{saved.Id}", OrderApiMapper.ToResponse(saved));
    }
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedPaymentMethodService>
{
    private readonly IHttpContextAccessor _http;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor http)
    {
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedPaymentMethodService methods) =>
            {
                return await HandleAsync(methods);
            })
            .Produces<PaymentMethodListResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ISavedPaymentMethodService methods)
    {
        var buyerId = _http.HttpContext!.RequireBuyerId();
        var list = await methods.ListAsync(buyerId);
        return Results.Ok(new PaymentMethodListResponse
        {
            PaymentMethods = list.Select(OrderApiMapper.ToResponse).ToList()
        });
    }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedPaymentMethodService>
{
    private readonly IHttpContextAccessor _http;

    public DeletePaymentMethodEndpoint(IHttpContextAccessor http)
    {
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedPaymentMethodService methods) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId }, methods);
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedPaymentMethodService methods)
    {
        var buyerId = _http.HttpContext!.RequireBuyerId();
        await methods.DeleteAsync(buyerId, request.PaymentMethodId);
        return Results.NoContent();
    }
}
