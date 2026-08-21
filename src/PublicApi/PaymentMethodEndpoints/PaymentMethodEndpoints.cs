using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreatePaymentMethodEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, HttpContext http, ISavedPaymentMethodService paymentMethods) =>
            {
                request.BuyerId = EndpointUser.RequireBuyerId(http);
                return await HandleAsync(request, paymentMethods);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService paymentMethods)
    {
        var user = await _userManager.FindByNameAsync(request.BuyerId!);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var billing = request.Card?.BillingAddress ?? new CardBillingAddressRequest();
        var card = new CardDetails(
            request.Card?.Number ?? string.Empty,
            request.Card?.Expiry ?? string.Empty,
            request.Card?.SecurityCode ?? string.Empty,
            request.Card?.Name ?? string.Empty,
            new CardBillingAddress(
                billing.AddressLine1 ?? string.Empty,
                billing.AdminArea2,
                billing.AdminArea1,
                billing.PostalCode ?? string.Empty,
                billing.CountryCode ?? string.Empty));

        var saved = await paymentMethods.SaveAsync(request.BuyerId!, user.Id, card);
        var response = new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = PaymentMethodDtoMapper.ToDto(saved)
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, ISavedPaymentMethodService paymentMethods) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = EndpointUser.RequireBuyerId(http) }, paymentMethods);
            })
            .Produces<ListPaymentMethodsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedPaymentMethodService paymentMethods)
    {
        var methods = await paymentMethods.ListAsync(request.BuyerId!);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = methods.Select(PaymentMethodDtoMapper.ToDto).ToList()
        });
    }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, HttpContext http, ISavedPaymentMethodService paymentMethods) =>
            {
                return await HandleAsync(
                    new DeletePaymentMethodRequest
                    {
                        PaymentMethodId = paymentMethodId,
                        BuyerId = EndpointUser.RequireBuyerId(http)
                    },
                    paymentMethods);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedPaymentMethodService paymentMethods)
    {
        await paymentMethods.DeleteAsync(request.BuyerId!, request.PaymentMethodId);
        return Results.NoContent();
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string? BuyerId { get; set; }
    public CardPaymentRequest? Card { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsRequest : BaseRequest
{
    public string? BuyerId { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public System.Collections.Generic.List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
    public string? BuyerId { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public static class PaymentMethodDtoMapper
{
    public static PaymentMethodDto ToDto(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        LastDigits = method.LastDigits,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName,
        DisplayName = $"{method.Brand} ending {method.LastDigits} (exp {method.Expiry})"
    };
}
