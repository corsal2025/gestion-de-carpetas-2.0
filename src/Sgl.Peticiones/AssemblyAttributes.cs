using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;

// Las páginas Razor de este ensamblado se compilan al build: hay que leerlas como partes compiladas,
// si no ASP.NET no registra sus rutas.
[assembly: ProvideApplicationPartFactory(typeof(CompiledRazorAssemblyApplicationPartFactory))]
