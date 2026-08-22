using System;
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
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(request, contactNumberService);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumberService)
    {
        var response = new CreateContactNumberResponse(request.CorrelationId());
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is not available.");
        var contact = await contactNumberService.RegisterAsync(httpContext.GetRequiredBuyerId(), request.PhoneNumber);
        response.ContactNumberId = contact.Id;
        response.PhoneNumber = contact.PhoneNumberE164;
        response.NationalFormat = contact.NationalFormat;
        return Results.Created($"api/contact-numbers/{contact.Id}", response);
    }
}
