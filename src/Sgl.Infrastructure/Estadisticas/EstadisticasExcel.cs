using ClosedXML.Excel;
using Sgl.Application.Estadisticas;

namespace Sgl.Infrastructure.Estadisticas;

/// <summary>Exports every statistics table to one workbook (aggregates only, no personal data).</summary>
public static class EstadisticasExcel
{
    public static byte[] Generar(EstadisticasDto e)
    {
        ArgumentNullException.ThrowIfNull(e);
        using var wb = new XLWorkbook();

        Hoja(wb, "Resumen", ["Indicador", "Valor"], [
            ["Subidas este mes", e.Kpis.SubidasEsteMes],
            ["Carpetas atrasadas (semáforo rojo)", e.Kpis.CarpetasAtrasadas],
            ["Búsquedas abiertas", e.Kpis.BusquedasAbiertas],
            ["Mediana días citación → subida", e.Kpis.MedianaDiasCitacionSubida],
            ["Personas con más de una carpeta abierta", e.Actuales.PersonasConVariasAbiertas],
        ]);
        Hoja(wb, "Subidas por mes", ["Año", "Mes", "Sede", "Conaset", "F8", "Oficio", "Correo", "Otra", "Total"],
            e.Actuales.SubidasPorMes.Select(s => new object?[] { s.Anio, s.Mes, s.Sede.ToString(), s.Conaset, s.F8, s.Oficio, s.Correo, s.Otra, s.Total }));
        Hoja(wb, "Estado por sede", ["Sede", "Estado", "Carpetas"],
            e.Actuales.EstadoPorSede.Select(s => new object?[] { s.Sede.ToString(), s.Clave, s.Cantidad }));
        Hoja(wb, "Citación a subida", ["Muestras", "Promedio (días)", "Mediana (días)", "Máximo (días)"],
            [[e.Actuales.CitacionSubida.Muestras, e.Actuales.CitacionSubida.Promedio, e.Actuales.CitacionSubida.Mediana, e.Actuales.CitacionSubida.Maximo]]);
        Hoja(wb, "Más lentas", ["Sede", "Citación", "Subida", "Días"],
            e.Actuales.MasLentas.Select(l => new object?[] { l.Sede.ToString(), l.FechaCitacion.ToString("yyyy-MM-dd"), l.FechaSubida.ToString("yyyy-MM-dd"), l.Dias }));
        Hoja(wb, "Citadas sin subir", ["Sede", $"Sin subir > {e.Actuales.DiasSinSubir} días"],
            e.Actuales.CitadasSinSubir.Select(s => new object?[] { s.Sede.ToString(), s.Cantidad }));
        Hoja(wb, "Decisión por sede", ["Sede", "Otorgados", "Denegados", "Tasa otorgamiento"],
            e.Actuales.DecisionPorSede.Select(d => new object?[] { d.Sede.ToString(), d.Otorgados, d.Denegados, d.TasaOtorgamiento }));
        Hoja(wb, "Decisión por mes", ["Año", "Mes", "Otorgados", "Denegados"],
            e.Actuales.DecisionPorMes.Select(d => new object?[] { d.Anio, d.Mes, d.Otorgados, d.Denegados }));
        Hoja(wb, "Cambios de domicilio", ["Sede", "Solicitados", "Subidos"],
            e.Actuales.CambiosDomicilio.Select(c => new object?[] { c.Sede.ToString(), c.Solicitados, c.Subidos }));
        Hoja(wb, "Comunas", ["Comuna", "Carpetas"], e.Actuales.ComunasTop.Select(Fila));
        var b = e.Busquedas;
        Hoja(wb, "Búsquedas", ["Indicador", "Valor"], [
            ["Iniciadas", b.Iniciadas], ["Encontradas", b.Encontradas], ["No encontradas", b.NoEncontradas],
            ["Abiertas", b.Abiertas], ["Abiertas vencidas", b.AbiertasVencidas],
            ["Horas para encontrar (promedio)", b.HorasParaEncontrar.Promedio], ["Horas para encontrar (mediana)", b.HorasParaEncontrar.Mediana],
            ["Horas para encontrar (máximo)", b.HorasParaEncontrar.Maximo],
            ["Encontradas en Archivo", b.EncontradasArchivo], ["Encontradas en Oficina 43", b.EncontradasOficina43],
        ]);
        Hoja(wb, "Archivo por mes", ["Año", "Mes", "Carpetas archivadas", "Cajas cerradas"],
            e.Archivo.PorMes.Select(a => new object?[] { a.Anio, a.Mes, a.CarpetasArchivadas, a.CajasCerradas }));
        Hoja(wb, "Cajas", ["Indicador", "Valor"], [
            ["Cajas con carpetas", e.Archivo.CajasConCarpetas], ["Ocupación promedio", e.Archivo.OcupacionPromedio],
            ["Cajas reabiertas", e.Archivo.CajasReabiertas], ["En cola de caja", e.Archivo.EnColaDeCaja],
        ]);
        Hoja(wb, "Cambios por funcionario", ["Funcionario", "Cambios"], e.Uso.CambiosPorFuncionario.Select(Fila));
        Hoja(wb, "Cambios por día", ["Día", "Cambios"], e.Uso.CambiosPorDia.Select(Fila));
        Hoja(wb, "Importaciones", ["Fecha", "Usuario", "Filas leídas", "Creadas", "Actualizadas", "Errores"],
            e.Uso.Importaciones.Select(i => new object?[] { i.Fecha.ToString("yyyy-MM-dd HH:mm"), i.Usuario, i.FilasLeidas, i.Creadas, i.Actualizadas, i.Errores }));
        Hoja(wb, "Origen y observaciones", ["Indicador", "Valor"], [
            ["Registradas a mano", e.Uso.RegistradasAMano], ["Importadas", e.Uso.Importadas],
            ["Carpetas con observación", e.Uso.CarpetasConObservacion],
        ]);
        Hoja(wb, "Términos observación", ["Término", "Carpetas"], e.Uso.TerminosObservacion.Select(Fila));
        Hoja(wb, "Clase de licencia", ["Sede", "Con clase", "Sin clase", "% con clase"],
            e.Uso.ClasePorSede.Select(c => new object?[] { c.Sede.ToString(), c.ConClase, c.SinClase, c.Porcentaje }));

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static object?[] Fila(ConteoDto c) => [c.Clave, c.Cantidad];

    private static void Hoja(XLWorkbook wb, string nombre, string[] encabezados, IEnumerable<object?[]> filas)
    {
        var ws = wb.Worksheets.Add(nombre);
        for (var i = 0; i < encabezados.Length; i++)
        {
            ws.Cell(1, i + 1).Value = encabezados[i];
        }

        ws.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var fila in filas)
        {
            for (var i = 0; i < fila.Length; i++)
            {
                ws.Cell(r, i + 1).Value = fila[i] switch
                {
                    null => Blank.Value,
                    int n => n,
                    double d => d,
                    _ => fila[i]!.ToString(),
                };
            }

            r++;
        }

        ws.Columns().AdjustToContents();
    }
}
