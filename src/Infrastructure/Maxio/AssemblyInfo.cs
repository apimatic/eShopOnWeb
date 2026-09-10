using System.Runtime.CompilerServices;

// Expose the internal Maxio client/models/service to the unit test project (and to
// NSubstitute's dynamic proxy assembly, so the internal IMaxioClient can be mocked).
[assembly: InternalsVisibleTo("UnitTests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
