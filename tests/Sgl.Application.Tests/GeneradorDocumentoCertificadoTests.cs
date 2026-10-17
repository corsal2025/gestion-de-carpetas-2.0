using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using Sgl.Application.Solicitudes;
using Sgl.Domain;
using Sgl.Infrastructure.Documentos;

namespace Sgl.Application.Tests;

public sealed class GeneradorDocumentoCertificadoTests
{
    // 2026-10-15 01:30 UTC is still 14 October in Chile (UTC-3): the emission date must follow Chile time.
    private static readonly FixedTimeProvider Reloj = new(new DateTimeOffset(2026, 10, 15, 1, 30, 0, TimeSpan.Zero));

    private static readonly Firmantes Firma = new("Ana Directora Prueba", "Director (S)", "dir@example.test", "Sara Secretaria Prueba", "Secretaria Municipal I.M.V");

    private readonly GeneradorDocumentoCertificado _generador = new(Reloj);

    private static DatosCertificado Datos(
        TipoCertificado tipo, Dictionary<string, string>? parametros = null, string? oficio = null, string rut = "12.345.678-5") =>
        new(tipo, rut, "Juan Pérez Prueba", oficio, parametros ?? []);

    private static string TextoCompleto(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        var partes = new List<string> { doc.MainDocumentPart!.Document.InnerText };
        partes.AddRange(doc.MainDocumentPart.HeaderParts.Select(h => h.Header.InnerText));
        partes.AddRange(doc.MainDocumentPart.FooterParts.Select(f => f.Footer.InnerText));
        return string.Join("\n", partes);
    }

    private static readonly Dictionary<string, string> ParamsSinCarpeta = new() { ["AnioRenovacion"] = "1995" };
    private static readonly Dictionary<string, string> ParamsRegistro = new() { ["Clase"] = "a2", ["Ley"] = "19.495", ["FechaOtorgamiento"] = "08-02-2017" };
    private static readonly Dictionary<string, string> ParamsOficio = new() { ["Clase"] = "b", ["Motivo"] = "motivo de prueba" };

    [Fact]
    public void SinCarpeta_fills_every_placeholder_and_names_the_file_after_the_rut()
    {
        var doc = _generador.Generar(Datos(TipoCertificado.SinCarpeta, ParamsSinCarpeta), Firma);
        var texto = TextoCompleto(doc.Contenido);

        Assert.Equal("CERTIFICADO-SIN-CARPETA-12345678-5.docx", doc.NombreArchivo);
        Assert.Contains("JUAN PÉREZ PRUEBA", texto);
        Assert.Contains("RUN 12.345.678-5", texto);
        Assert.Contains("en el año 1995", texto);
        Assert.Contains("ANA DIRECTORA PRUEBA, director (s) de Tránsito", texto);
        Assert.Contains("Sara Secretaria Prueba", texto);
        Assert.Contains("Secretaria Municipal I.M.V", texto);
        Assert.DoesNotContain("{{", texto);
    }

    [Fact]
    public void RegistroCivil_uses_spanish_long_dates_and_the_chile_emission_date()
    {
        var doc = _generador.Generar(Datos(TipoCertificado.RegistroCivil, ParamsRegistro), Firma);
        var texto = TextoCompleto(doc.Contenido);

        Assert.Equal("CERTIFICADO-REGISTRO-CIVIL-12345678-5.docx", doc.NombreArchivo);
        Assert.Contains("Clase A2 (Ley N° 19.495) con fecha 8 de febrero del 2017.", texto);
        Assert.Contains("14 de octubre del 2026", texto);
        Assert.Contains("DON JUAN PÉREZ PRUEBA RUN 12.345.678-5", texto);
        Assert.Contains("ANA DIRECTORA PRUEBA, director (s) de Tránsito", texto);
        Assert.DoesNotContain("{{", texto);
    }

    [Theory]
    [InlineData("2017-02-08")]
    [InlineData("08-02-2017")]
    [InlineData("8/2/2017")]
    public void RegistroCivil_accepts_the_common_date_formats(string entrada)
    {
        var p = new Dictionary<string, string>(ParamsRegistro) { ["FechaOtorgamiento"] = entrada };
        var texto = TextoCompleto(_generador.Generar(Datos(TipoCertificado.RegistroCivil, p), Firma).Contenido);

        Assert.Contains("con fecha 8 de febrero del 2017.", texto);
    }

    [Fact]
    public void Oficio_fills_number_date_class_motive_and_the_signer()
    {
        var doc = _generador.Generar(Datos(TipoCertificado.Oficio, ParamsOficio, "310/2026", "17.475.823-9"), Firma);
        var texto = TextoCompleto(doc.Contenido);

        Assert.Equal("OFICIO-310-2026-17475823-9.docx", doc.NombreArchivo);
        Assert.Contains("ORDINARIO DT Nº 310/2026", texto);
        Assert.Contains("Valparaíso, 14 de octubre del 2026", texto);
        Assert.Contains("eliminar licencia clase B que", texto);
        Assert.Contains("JUAN PÉREZ PRUEBA RUT. 17.475.823-9.", texto);
        Assert.Contains("fue denegada por motivo de prueba. Por ende", texto);
        Assert.Contains("ANA DIRECTORA PRUEBA", texto);
        Assert.Contains("DIRECTOR (S)", texto);
        Assert.DoesNotContain("{{", texto);
    }

    [Fact]
    public void Oficio_number_without_year_gets_the_current_year()
    {
        var texto = TextoCompleto(_generador.Generar(Datos(TipoCertificado.Oficio, ParamsOficio, "310"), Firma).Contenido);

        Assert.Contains("ORDINARIO DT Nº 310/2026", texto);
    }

    [Fact]
    public void SinCarpeta_without_the_renewal_year_fails_with_a_clear_message()
    {
        var ex = Assert.Throws<DomainException>(() => _generador.Generar(Datos(TipoCertificado.SinCarpeta), Firma));

        Assert.Contains("año de la última renovación", ex.Message);
    }

    [Theory]
    [InlineData("abcd")]
    [InlineData("1850")]
    [InlineData("2200")]
    public void SinCarpeta_with_an_invalid_year_fails(string anio)
    {
        var p = new Dictionary<string, string> { ["AnioRenovacion"] = anio };

        Assert.Throws<DomainException>(() => _generador.Generar(Datos(TipoCertificado.SinCarpeta, p), Firma));
    }

    [Theory]
    [InlineData("Clase")]
    [InlineData("Ley")]
    [InlineData("FechaOtorgamiento")]
    public void RegistroCivil_requires_every_parameter(string faltante)
    {
        var p = new Dictionary<string, string>(ParamsRegistro);
        p.Remove(faltante);

        var ex = Assert.Throws<DomainException>(() => _generador.Generar(Datos(TipoCertificado.RegistroCivil, p), Firma));

        Assert.StartsWith("Falta", ex.Message);
    }

    [Fact]
    public void MotivoNoGenerable_reports_the_same_text_Generar_would_throw_and_null_when_ready()
    {
        var incompleto = Datos(TipoCertificado.SinCarpeta);

        Assert.Null(_generador.MotivoNoGenerable(Datos(TipoCertificado.SinCarpeta, ParamsSinCarpeta), Firma));
        Assert.Equal(
            Assert.Throws<DomainException>(() => _generador.Generar(incompleto, Firma)).Message,
            _generador.MotivoNoGenerable(incompleto, Firma));
    }

    [Fact]
    public void RegistroCivil_with_an_unparseable_date_fails()
    {
        var p = new Dictionary<string, string>(ParamsRegistro) { ["FechaOtorgamiento"] = "mañana" };

        Assert.Throws<DomainException>(() => _generador.Generar(Datos(TipoCertificado.RegistroCivil, p), Firma));
    }

    [Theory]
    [InlineData("Clase")]
    [InlineData("Motivo")]
    public void Oficio_requires_class_and_motive(string faltante)
    {
        var p = new Dictionary<string, string>(ParamsOficio);
        p.Remove(faltante);

        Assert.Throws<DomainException>(() => _generador.Generar(Datos(TipoCertificado.Oficio, p, "1/2026"), Firma));
    }

    [Fact]
    public void Oficio_requires_the_number()
    {
        var ex = Assert.Throws<DomainException>(() => _generador.Generar(Datos(TipoCertificado.Oficio, ParamsOficio), Firma));

        Assert.Contains("número de oficio", ex.Message);
    }

    [Fact]
    public void Missing_director_signer_is_refused_for_every_tipo()
    {
        var sinDirector = Firma with { DirectorNombre = "" };

        foreach (var (tipo, p) in new[]
                 {
                     (TipoCertificado.SinCarpeta, ParamsSinCarpeta),
                     (TipoCertificado.RegistroCivil, ParamsRegistro),
                     (TipoCertificado.Oficio, ParamsOficio),
                 })
        {
            var ex = Assert.Throws<DomainException>(() => _generador.Generar(Datos(tipo, p, "1/2026"), sinDirector));
            Assert.Contains("Firmantes", ex.Message);
        }
    }

    [Fact]
    public void Missing_secretaria_is_refused_only_where_the_model_has_one()
    {
        var sinSecretaria = Firma with { SecretariaNombre = "", SecretariaCargo = "" };

        Assert.Throws<DomainException>(() => _generador.Generar(Datos(TipoCertificado.SinCarpeta, ParamsSinCarpeta), sinSecretaria));
        _generador.Generar(Datos(TipoCertificado.RegistroCivil, ParamsRegistro), sinSecretaria);
        _generador.Generar(Datos(TipoCertificado.Oficio, ParamsOficio, "1/2026"), sinSecretaria);
    }

    [Fact]
    public void Special_characters_in_the_values_are_escaped_and_cannot_inject_placeholders()
    {
        var p = new Dictionary<string, string>(ParamsOficio) { ["Motivo"] = "R&D <b> {{CLASE}}" };
        var doc = _generador.Generar(Datos(TipoCertificado.Oficio, p, "1/2026"), Firma);

        var texto = TextoCompleto(doc.Contenido);

        Assert.Contains("R&D <b> {{CLASE}}", texto);
    }

    [Theory]
    [InlineData("sin-carpeta")]
    [InlineData("registro-civil")]
    [InlineData("oficio")]
    public void Committed_templates_hold_only_placeholders_and_no_RUT_like_data(string nombre)
    {
        using var stream = typeof(GeneradorDocumentoCertificado).Assembly.GetManifestResourceStream($"Plantillas.{nombre}.docx");
        Assert.NotNull(stream);
        using var doc = WordprocessingDocument.Open(stream, false);
        var todo = new List<string> { doc.MainDocumentPart!.Document.InnerText };
        todo.AddRange(doc.MainDocumentPart.HeaderParts.Select(h => h.Header.InnerText));
        todo.AddRange(doc.MainDocumentPart.FooterParts.Select(f => f.Footer.InnerText));
        var texto = string.Join("\n", todo);

        Assert.DoesNotMatch(new Regex(@"\d{1,2}\.\d{3}\.\d{3}-[\dkK]"), texto);
        Assert.DoesNotMatch(new Regex(@"\b\d{7,8}-[\dkK]\b"), texto);
        Assert.Contains("{{NOMBRE}}", texto);
        Assert.Contains("{{DIRECTOR_NOMBRE}}", texto);
    }
}

public sealed class DocumentoCertificadoServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private DocumentoCertificadoService NuevoServicio() =>
        new(_db.NewCertificadosService(), _db.NewFirmantesService(), new GeneradorDocumentoCertificado(_db.Clock));

    private async Task<Guid> CrearOficioAsync()
    {
        var carpeta = await SolicitudesTestSupport.CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);
        var creado = await _db.NewCertificadosService().CrearManualAsync(new CrearCertificadoManualCommand(
            carpeta, TipoCertificado.Oficio, new Dictionary<string, string> { ["Clase"] = "B", ["Motivo"] = "x" }, "7/2026", "ana"));
        return creado.Id;
    }

    [Fact]
    public async Task Builds_the_document_of_a_stored_request_with_the_configured_signers()
    {
        await _db.NewFirmantesService().GuardarAsync(new Firmantes("Ana Directora Prueba", "Director", "", "", ""));
        var id = await CrearOficioAsync();

        var doc = await NuevoServicio().GenerarAsync(id);

        Assert.Equal("OFICIO-7-2026-12345678-5.docx", doc.NombreArchivo);
        Assert.True(doc.Contenido.Length > 1000);
    }

    [Fact]
    public async Task Without_configured_signers_it_fails_with_a_message_for_the_user()
    {
        var id = await CrearOficioAsync();

        var ex = await Assert.ThrowsAsync<DomainException>(() => NuevoServicio().GenerarAsync(id));

        Assert.Contains("Firmantes", ex.Message);
    }

    [Fact]
    public async Task Unknown_request_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => NuevoServicio().GenerarAsync(Guid.NewGuid()));
    }
}
