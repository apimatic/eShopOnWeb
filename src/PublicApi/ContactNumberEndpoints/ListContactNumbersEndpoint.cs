using System;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public record ContactNumberDto(int ContactNumberId, string CanonicalNumber, DateTimeOffset RegisteredAt);

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(Guid correlationId) : base(correlationId) { }

    public IReadOnlyList<ContactNumberDto> ContactNumbers { get; set; } = new List<ContactNumberDto>();
}

/// <summary>Returns the caller's own registered numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, ListContactNumbersRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISmsNotificationService service) =>
                await HandleAsync(new ListContactNumbersRequest { BuyerId = user.GetBuyerId() }, service))
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, ISmsNotificationService service)
    {
        var numbers = await service.GetContactNumbersAsync(request.BuyerId);
        var response = new ListContactNumbersResponse(request.CorrelationId())
        {
            ContactNumbers = numbers
                .Select(n => new ContactNumberDto(n.Id, n.CanonicalNumber, n.RegisteredAt))
                .ToList()
        };
        return Results.Ok(response);
    }
}
