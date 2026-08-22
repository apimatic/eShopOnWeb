using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(System.Guid correlationId) : base(correlationId) { }
    public CreateContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateContactNumberRequest request, IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(request, contactNumberService);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumberService)
    {
        var buyerId = BuyerIdentity.RequireBuyerId(_httpContextAccessor.HttpContext!);
        var contact = await contactNumberService.RegisterAsync(buyerId, request.PhoneNumber);
        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contact.Id,
            PhoneNumber = contact.CanonicalNumber
        };

        return Results.Created($"api/contact-numbers/{contact.Id}", response);
    }
}

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ListContactNumbersEndpoint : IEndpoint<IResult, IContactNumberService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListContactNumbersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(contactNumberService);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(IContactNumberService contactNumberService)
    {
        var buyerId = BuyerIdentity.RequireBuyerId(_httpContextAccessor.HttpContext!);
        var numbers = await contactNumberService.ListAsync(buyerId);
        var response = new ListContactNumbersResponse();
        foreach (var number in numbers)
        {
            response.ContactNumbers.Add(new ContactNumberDto
            {
                ContactNumberId = number.Id,
                PhoneNumber = number.CanonicalNumber
            });
        }

        return Results.Ok(response);
    }
}

public class DeleteContactNumberRequest : BaseRequest
{
    public int ContactNumberId { get; init; }
    public DeleteContactNumberRequest(int contactNumberId) => ContactNumberId = contactNumberId;
}

public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeleteContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int contactNumberId, IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), contactNumberService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService contactNumberService)
    {
        var buyerId = BuyerIdentity.RequireBuyerId(_httpContextAccessor.HttpContext!);
        await contactNumberService.DeleteAsync(buyerId, request.ContactNumberId);
        return Results.NoContent();
    }
}
