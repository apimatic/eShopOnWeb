using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

public class MbPagination
{
    public int TotalResults { get; set; }
    public int PageSize { get; set; }
}

public class MbPage<T>
{
    public MbPagination? PaginationResponse { get; set; }
    public List<T> Items { get; set; } = new();
}

public class MbError
{
    public string? Message { get; set; }
    public string? Code { get; set; }
}

public class MbLocation
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

public class MbStaff
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? MobilePhone { get; set; }
    public string? HomePhone { get; set; }
    public string? WorkPhone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
    public string? Bio { get; set; }
    public string? ImageUrl { get; set; }
    public bool Active { get; set; }
    public bool AppointmentInstructor { get; set; }
    public bool ClassTeacher { get; set; }
}

public class MbClassDescription
{
    public string? Name { get; set; }
}

public class MbClass
{
    public int Id { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public bool IsCanceled { get; set; }
    public bool Active { get; set; }
    public bool IsAvailable { get; set; }
    public int MaxCapacity { get; set; }
    public int TotalBooked { get; set; }
    public int WebCapacity { get; set; }
    public int WebBooked { get; set; }
    public MbClassDescription? ClassDescription { get; set; }
    public MbLocation? Location { get; set; }
    public MbStaff? Staff { get; set; }
}

public class MbAvailability
{
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public MbStaff? Staff { get; set; }
    public MbLocation? Location { get; set; }
}

public class MbSessionType
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public int? DefaultTimeLength { get; set; }
}

public class MbClient
{
    public string? Id { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class MbVisit
{
    public long Id { get; set; }
    public long AppointmentId { get; set; }
    public int ClassId { get; set; }
    public string? AppointmentStatus { get; set; }
    public bool LateCancelled { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public int LocationId { get; set; }
    public long StaffId { get; set; }
    public int VisitType { get; set; }
    public string? Name { get; set; }
}

public class MbAppointment
{
    public long Id { get; set; }
    public string? Status { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public int SessionTypeId { get; set; }
    public int LocationId { get; set; }
    public long StaffId { get; set; }
    public string? ClientId { get; set; }
}
