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

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, PayOrderCardRequest, ClaimsPrincipal>
{
    private readonly ISavedPaymentMethodService _savedPaymentMethodService;

    public CreatePaymentMethodEndpoint(ISavedPaymentMethodService savedPaymentMethodService)
    {
        _savedPaymentMethodService = savedPaymentMethodService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PayOrderCardRequest request, ClaimsPrincipal user) => await HandleAsync(request, user))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderCardRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.RequireUserName();
        var saved = await _savedPaymentMethodService.SaveCardAsync(buyerId, CardMapper.ToCardPaymentSource(request));
        var response = PaymentMethodDto.From(saved);
        return Results.Created($"api/payment-methods/{saved.Id}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = response
        });
    }
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly ISavedPaymentMethodService _savedPaymentMethodService;

    public ListPaymentMethodsEndpoint(ISavedPaymentMethodService savedPaymentMethodService)
    {
        _savedPaymentMethodService = savedPaymentMethodService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) => await HandleAsync(user))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.RequireUserName();
        var items = await _savedPaymentMethodService.ListAsync(buyerId);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = items.Select(PaymentMethodDto.From).ToList()
        });
    }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly ISavedPaymentMethodService _savedPaymentMethodService;

    public DeletePaymentMethodEndpoint(ISavedPaymentMethodService savedPaymentMethodService)
    {
        _savedPaymentMethodService = savedPaymentMethodService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user) => await HandleAsync(paymentMethodId, user))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, ClaimsPrincipal user)
    {
        var buyerId = user.RequireUserName();
        await _savedPaymentMethodService.DeleteAsync(buyerId, paymentMethodId);
        return Results.NoContent();
    }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }

    public static PaymentMethodDto From(ApplicationCore.Entities.SavedPaymentMethodAggregate.SavedPaymentMethod saved) => new()
    {
        PaymentMethodId = saved.Id,
        Brand = saved.Brand,
        LastDigits = saved.LastDigits,
        Expiry = saved.Expiry,
        CardholderName = saved.CardholderName
    };
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsResponse
{
    public System.Collections.Generic.List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}
