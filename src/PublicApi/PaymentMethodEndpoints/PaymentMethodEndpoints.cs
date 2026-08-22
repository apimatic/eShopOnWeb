using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? Name { get; set; }
}

public static class PaymentMethodDtoMapper
{
    public static PaymentMethodDto Map(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        LastDigits = method.Last4,
        Brand = method.Brand,
        Expiry = method.Expiry,
        Name = method.Alias
    };
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string? BuyerId { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public PayOrderAddressRequest? BillingAddress { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, IOrderPaymentService service, HttpContext httpContext) =>
            {
                request.BuyerId = CreateOrderEndpoint.RequireBuyerId(httpContext);
                return await HandleAsync(request, service);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IOrderPaymentService service)
    {
        var billing = request.BillingAddress;
        var card = new CardPaymentInput(
            request.Number,
            request.Expiry,
            request.SecurityCode,
            request.Name,
            billing == null
                ? new BillingAddressInput("123 Main St.", null, "Kent", "OH", "44240", "US")
                : new BillingAddressInput(
                    billing.AddressLine1,
                    billing.AddressLine2,
                    billing.AdminArea2,
                    billing.AdminArea1,
                    billing.PostalCode,
                    string.IsNullOrWhiteSpace(billing.CountryCode) ? "US" : billing.CountryCode));

        var method = await service.SaveCardAsync(request.BuyerId!, card, default);
        var dto = PaymentMethodDtoMapper.Map(method);
        return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = dto.PaymentMethodId,
            PaymentMethod = dto
        });
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
    public string? BuyerId { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public System.Collections.Generic.List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService service, HttpContext httpContext) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = CreateOrderEndpoint.RequireBuyerId(httpContext) }, service);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IOrderPaymentService service)
    {
        var methods = await service.ListCardsAsync(request.BuyerId!, default);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = methods.Select(PaymentMethodDtoMapper.Map).ToList()
        });
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public string? BuyerId { get; set; }
    public int PaymentMethodId { get; set; }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, IOrderPaymentService service, HttpContext httpContext) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest
                {
                    BuyerId = CreateOrderEndpoint.RequireBuyerId(httpContext),
                    PaymentMethodId = paymentMethodId
                }, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IOrderPaymentService service)
    {
        await service.DeleteCardAsync(request.BuyerId!, request.PaymentMethodId, default);
        return Results.NoContent();
    }
}
