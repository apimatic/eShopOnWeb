using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class RequestValidationException : Exception
{
    public RequestValidationException(string message) : base(message) { }
}

public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message) { }
}

public class ResourceConflictException : Exception
{
    public ResourceConflictException(string message) : base(message) { }
}
