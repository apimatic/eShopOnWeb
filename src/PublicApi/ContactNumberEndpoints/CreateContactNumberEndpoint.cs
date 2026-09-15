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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, CreateContactNumberRequest request, IContactNumberService contactNumberService) =>
            {
                if (string.IsNullOrWhiteSpace(request.PhoneNumber))
                {
                    return Results.BadRequest(new { message = "PhoneNumber is required." });
                }

                var contact = await contactNumberService.RegisterAsync(httpContext.GetBuyerId(), request.PhoneNumber);
                var response = new CreateContactNumberResponse(request.CorrelationId())
                {
                    ContactNumberId = contact.Id,
                    PhoneNumber = contact.PhoneNumber,
                    NationalFormat = contact.NationalFormat
                };

                return Results.Created($"api/contact-numbers/{contact.Id}", response);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumberService) =>
        throw new System.NotSupportedException();
}
