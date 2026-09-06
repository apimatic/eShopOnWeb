using System.Runtime.CompilerServices;

// The Maxio billing client and its wire contracts are internal: nothing outside this assembly should
// bind to the provider's shapes. The unit tests need them, and NSubstitute needs to proxy the
// internal client interface.
[assembly: InternalsVisibleTo("UnitTests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
