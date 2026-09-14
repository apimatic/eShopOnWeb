using System.Runtime.CompilerServices;

// Allows unit tests to substitute internal Maxio abstractions (IMaxioApiClient, IUserOperationLock)
// without making them part of Infrastructure's public surface.
[assembly: InternalsVisibleTo("UnitTests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
