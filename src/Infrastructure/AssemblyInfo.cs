using System.Runtime.CompilerServices;

// Exposes internal types (e.g. the Maxio base-url override handler) to the integration test project.
[assembly: InternalsVisibleTo("IntegrationTests")]
