using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using PeticionCambioDomicilio.Pages;

namespace Sgl.Application.Tests;

/// <summary>E13: /peticiones pasa a «Historial de peticiones (anterior)», solo lectura (sin importar, enviar ni reenviar).</summary>
public sealed class HistorialSoloLecturaTests
{
    private static PageHandlerExecutingContext Contexto(string metodo, out IndexModel modelo)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = metodo;
        var pagina = new PageContext(new ActionContext(http, new RouteData(), new PageActionDescriptor()));
        modelo = (IndexModel)RuntimeHelpers.GetUninitializedObject(typeof(IndexModel));
        modelo.PageContext = pagina;
        return new PageHandlerExecutingContext(pagina, [], new HandlerMethodDescriptor(), new Dictionary<string, object?>(), modelo);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public void Cualquier_envio_de_formulario_se_rechaza_con_405(string metodo)
    {
        var contexto = Contexto(metodo, out var modelo);

        modelo.OnPageHandlerExecuting(contexto);

        var resultado = Assert.IsType<StatusCodeResult>(contexto.Result);
        Assert.Equal(StatusCodes.Status405MethodNotAllowed, resultado.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public void Consultar_la_pagina_sigue_permitido(string metodo)
    {
        var contexto = Contexto(metodo, out var modelo);

        modelo.OnPageHandlerExecuting(contexto);

        Assert.Null(contexto.Result);
    }

    [Fact]
    public void La_vista_no_trae_los_botones_de_importar_enviar_reenviar_ni_borrar()
    {
        var vista = File.ReadAllText(Path.Combine(RaizRepositorio(), "src", "Sgl.Peticiones", "Pages", "Index.cshtml"));

        Assert.Contains("Historial de peticiones (anterior)", vista);
        foreach (var prohibido in new[]
                 {
                     "btn-importar", "btn-enviar-marcadas", "btn-reenviar-marcadas", "btn-marcar-sin-correo", "btn-ver-ya-subidas",
                     "btn-borrar-seleccion", "btn-borrar-todo", "btn-peticion-manual", "asp-page-handler=\"EnviarComuna\"",
                     "asp-page-handler=\"Eliminar\"", "asp-page-handler=\"Importar\"",
                 })
        {
            Assert.DoesNotContain(prohibido, vista);
        }
    }

    private static string RaizRepositorio()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sgl.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
