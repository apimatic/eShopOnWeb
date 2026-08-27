using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, HttpContext httpContext, IContactNumberService contactNumberService) =>
            {
                var buyerId = EndpointIdentity.GetRequiredBuyerId(httpContext);
                return await HandleAsync(request with { BuyerId = buyerId }, contactNumberService);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumberService)
    {
        var contact = await contactNumberService.RegisterAsync(request.BuyerId, request.PhoneNumber);
        var response = new CreateContactNumberResponse
        {
            ContactNumberId = contact.Id,
            PhoneNumber = contact.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contact.Id}", response);
    }
}

public class ListContactNumbersEndpoint : IEndpoint<IResult, string, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IContactNumberService contactNumberService) =>
            {
                var buyerId = EndpointIdentity.GetRequiredBuyerId(httpContext);
                return await HandleAsync(buyerId, contactNumberService);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IContactNumberService contactNumberService)
    {
        var contacts = await contactNumberService.ListForBuyerAsync(buyerId);
        return Results.Ok(new ListContactNumbersResponse
        {
            ContactNumbers = contacts.Select(c => new ContactNumberDto
            {
                ContactNumberId = c.Id,
                PhoneNumber = c.PhoneNumber
            }).ToList()
        });
    }
}

public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext httpContext, IContactNumberService contactNumberService) =>
            {
                var buyerId = EndpointIdentity.GetRequiredBuyerId(httpContext);
                return await HandleAsync(new DeleteContactNumberRequest(buyerId, contactNumberId), contactNumberService);
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService contactNumberService)
    {
        await contactNumberService.DeleteAsync(request.BuyerId, request.ContactNumberId);
        return Results.Ok();
    }
}

public record CreateContactNumberRequest
{
    public string PhoneNumber { get; init; } = string.Empty;

    [JsonIgnore]
    public string BuyerId { get; init; } = string.Empty;
}

public class CreateContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

public record DeleteContactNumberRequest(string BuyerId, int ContactNumberId);
