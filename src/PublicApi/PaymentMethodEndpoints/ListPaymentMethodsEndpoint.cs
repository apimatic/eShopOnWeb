using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ListPaymentMethodsRequest
{
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest>
{
    private readonly CatalogContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListPaymentMethodsEndpoint(CatalogContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
                await HandleAsync(new ListPaymentMethodsRequest()))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request)
    {
        var ownerId = PaymentHelpers.OwnerId(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Results.Unauthorized();
        }

        var cards = await _db.SavedCards
            .Where(c => c.OwnerId == ownerId && !c.IsDeleted)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var response = new ListPaymentMethodsResponse(Guid.NewGuid());
        response.PaymentMethods.AddRange(cards.Select(c => new PaymentMethodDto
        {
            PaymentMethodId = c.Id,
            Brand = c.Brand,
            Last4 = c.Last4,
            ExpirationMonth = c.ExpirationMonth,
            ExpirationYear = c.ExpirationYear,
            Name = c.Name,
            CreatedAt = c.CreatedAt
        }));

        return Results.Ok(response);
    }
}
