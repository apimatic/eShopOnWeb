using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IShopperPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, ClaimsPrincipal user, IShopperPaymentMethodService methods) =>
                await HandleAsync(request, user, methods))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IShopperPaymentMethodService methods)
        => HandleAsync(request, new ClaimsPrincipal(), methods);

    public async Task<IResult> HandleAsync(
        CreatePaymentMethodRequest request,
        ClaimsPrincipal user,
        IShopperPaymentMethodService methods)
    {
        var buyerId = CallerIdentity.GetBuyerId(user);
        var card = request.Card ?? throw new ApplicationCore.Exceptions.PaymentValidationException("Card details are required.");
        var saved = await methods.SaveCardAsync(buyerId, new CardPaymentRequest
        {
            Number = card.Number ?? string.Empty,
            Expiry = card.Expiry ?? string.Empty,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress is null
                ? null
                : new BillingAddressRequest
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                }
        });

        var dto = PaymentMethodDto.From(saved);
        return Results.Created($"api/payment-methods/{saved.Id}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = dto
        });
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsRequest? Card { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, string, IShopperPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IShopperPaymentMethodService methods) =>
                await HandleAsync(CallerIdentity.GetBuyerId(user), methods))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IShopperPaymentMethodService methods)
    {
        var list = await methods.ListAsync(buyerId);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = list.Select(PaymentMethodDto.From).ToList()
        });
    }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, IShopperPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ClaimsPrincipal user, IShopperPaymentMethodService methods) =>
            {
                await methods.DeleteAsync(CallerIdentity.GetBuyerId(user), paymentMethodId);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int paymentMethodId, IShopperPaymentMethodService methods)
        => Task.FromResult(Results.NoContent());
}
