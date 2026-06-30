using System.Runtime.CompilerServices;

// Allow the Application layer to use pre-persistence factories.
[assembly: InternalsVisibleTo("Pico.Application")]
[assembly: InternalsVisibleTo("Pico.Infrastructure")]
[assembly: InternalsVisibleTo("Pico.Api")]
[assembly: InternalsVisibleTo("Pico.Tests")]
