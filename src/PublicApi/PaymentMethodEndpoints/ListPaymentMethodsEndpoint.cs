using System;
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

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// The caller's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentMethodService paymentMethodService, CancellationToken ct) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest(user.Identity?.Name ?? string.Empty), paymentMethodService, ct);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentMethodService paymentMethodService)
    {
        return HandleAsync(request, paymentMethodService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentMethodService paymentMethodService, CancellationToken ct)
    {
        try
        {
            var cards = await paymentMethodService.ListCardsAsync(request.BuyerId, ct);

            var response = new ListPaymentMethodsResponse(request.CorrelationId())
            {
                PaymentMethods = cards.Select(c => new PaymentMethodDto
                {
                    PaymentMethodId = c.Id,
                    Brand = c.Brand,
                    LastDigits = c.LastDigits,
                    Expiry = c.Expiry,
                    CreatedOn = c.CreatedOn
                }).ToList()
            };
            return Results.Ok(response);
        }
        catch (Exception ex) when (EndpointErrorMapper.TryMap(ex, out var error))
        {
            return error;
        }
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
    public ListPaymentMethodsRequest(string buyerId)
    {
        BuyerId = buyerId;
    }

    public string BuyerId { get; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}
