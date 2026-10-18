using Sgl.Application.SincronizacionCorreo;
using Xunit;

namespace Sgl.Application.Tests;

// Ported from CambioDeDomicilio.Tests/Extraction/PersonDataExtractorTests.cs.

public class PersonDataExtractorTests
{
    [Fact]
    public void Extract_PrefixedRut_ExtractsAdjacentName()
    {
        var body = "Se solicita la carpeta de GUSTAVO ANDRÉS PEÑA CASTRO RUT: 18.785.387-7 por cambio de domicilio.";

        var result = PersonDataExtractor.Extract(body);

        Assert.Equal("GUSTAVO ANDRÉS PEÑA CASTRO", result.FullName);
        Assert.Equal("18.785.387-7", result.Rut);
    }

    [Fact]
    public void Extract_HyphenatedCompoundFirstName_IsNotTruncated()
    {
        var body = "MARIA-ANTONIA RECULE RIVERA, RUT: 18.586.259-3.";

        var result = PersonDataExtractor.Extract(body);

        Assert.Equal("MARIA-ANTONIA RECULE RIVERA", result.FullName);
        Assert.Equal("18.586.259-3", result.Rut);
    }

    [Fact]
    public void Extract_RunPrefixVariant_IsRecognized()
    {
        var body = "carpeta de don Gustavo Andrés Peña Castro RUN 18785387-7, gracias.";

        var result = PersonDataExtractor.Extract(body);

        Assert.Equal("Gustavo Andrés Peña Castro", result.FullName);
        Assert.Equal("18.785.387-7", result.Rut);
    }

    [Fact]
    public void Extract_BareDottedRutWithoutPrefix_IsRecognized()
    {
        var body = "Junto con saludar, solicito la carpeta de GUSTAVO ANDRÉS PEÑA CASTRO 18.785.387-7 para tramitación.";

        var result = PersonDataExtractor.Extract(body);

        Assert.Equal("GUSTAVO ANDRÉS PEÑA CASTRO", result.FullName);
        Assert.Equal("18.785.387-7", result.Rut);
    }

    [Fact]
    public void Extract_BareUndottedRut_IsRecognizedAndNormalized()
    {
        var body = "solicita carpeta Gustavo Andrés Peña Castro 18785387-7";

        var result = PersonDataExtractor.Extract(body);

        Assert.Equal("18.785.387-7", result.Rut);
        Assert.Equal("Gustavo Andrés Peña Castro", result.FullName);
    }

    [Fact]
    public void Extract_ExternalBanner_IsNeverExtractedAsName()
    {
        var body = "CORREO EXTERNO : No haga clic en ningún enlace ni abra ningún archivo adjunto " +
                   "a menos que confíe en el remitente y sepa que el contenido es seguro. " +
                   "Solicito carpeta de GUSTAVO ANDRÉS PEÑA CASTRO RUT: 18.785.387-7";

        var result = PersonDataExtractor.Extract(body);

        Assert.Equal("GUSTAVO ANDRÉS PEÑA CASTRO", result.FullName);
        Assert.DoesNotContain("EXTERNO", result.FullName);
    }

    [Fact]
    public void Extract_NameAfterRut_IsFoundAsFallback()
    {
        var body = "Solicito carpeta del contribuyente con RUT: 18.785.387-7 GUSTAVO ANDRÉS PEÑA CASTRO.";

        var result = PersonDataExtractor.Extract(body);

        Assert.Equal("GUSTAVO ANDRÉS PEÑA CASTRO", result.FullName);
    }

    [Fact]
    public void Extract_HonorificsStrippedFromName()
    {
        var body = "carpeta de Don Gustavo Peña RUT 18.785.387-7";

        var result = PersonDataExtractor.Extract(body);

        Assert.Equal("Gustavo Peña", result.FullName);
    }

    [Fact]
    public void Extract_SpaceSeparatedCheckDigit_VinaFormat_ReordersToGivenNamesFirst()
    {
        // Verbatim shape of Viña del Mar's system output: undotted body, check digit after a
        // run of spaces, name AFTER the RUT padded with space runs and CRLFs, then filler text.
        // Viña's system always exports APELLIDO APELLIDO NOMBRE NOMBRE — reordered here to match
        // the given-name-first convention used everywhere else in the report/dashboard.
        var body = "solicitar los antecedentes correspondientes a:\r\n\r\n18785387        7       CARVAJAL        LUCERO  MATIAS JORGE\r\n\r\nQuien posee una licencia de conducir emitida en ese municipio.";

        var result = PersonDataExtractor.Extract(body);

        Assert.Equal("18.785.387-7", result.Rut);
        Assert.Equal("MATIAS JORGE CARVAJAL LUCERO", result.FullName);
    }

    [Fact]
    public void Extract_SpaceSeparatedThreeWordName_ReordersToGivenNameFirst()
    {
        // Single-given-name variant of Viña's export (APELLIDO APELLIDO NOMBRE, one fewer word
        // than the calibrated 4-word case) — same reorder rule, unambiguous either way.
        var body = "19328001        3       FIGUEROA        BOLADOS ZIZINHO";

        var result = PersonDataExtractor.Extract(body);

        Assert.Equal("19.328.001-3", result.Rut);
        Assert.Equal("ZIZINHO FIGUEROA BOLADOS", result.FullName);
    }

    [Fact]
    public void Extract_SpaceSeparatedAmbiguousWordCount_DropsNameForReview()
    {
        // 5 words can't be split into surnames/given-names reliably (could be 2+3 or 3+2) —
        // dropping the name (forcing NeedsReview via the null FullName) beats silently storing
        // it in the wrong order.
        var body = "19328001        3       SAN MARTIN GARCIA JUAN CARLOS";

        var result = PersonDataExtractor.Extract(body);

        Assert.Equal("19.328.001-3", result.Rut);
        Assert.Null(result.FullName);
    }

    [Fact]
    public void Extract_FourWordNameNotFromSpaceSeparatedFormat_IsNotReordered()
    {
        // The reorder is specific to Viña's fixed-order system export (space-separated check
        // digit) — free-text prose with a prefixed RUT keeps its natural given-name-first order
        // even when it happens to be four words.
        var body = "Se solicita la carpeta de GUSTAVO ANDRÉS PEÑA CASTRO RUT: 18.785.387-7 por cambio de domicilio.";

        var result = PersonDataExtractor.Extract(body);

        Assert.Equal("GUSTAVO ANDRÉS PEÑA CASTRO", result.FullName);
    }

    [Fact]
    public void ExtractAll_SpaceSeparatedFormat_ReordersEachContributor()
    {
        var body = "18785387        7       CARVAJAL        LUCERO  MATIAS JORGE\r\n12345678        5       SOTO    ROJAS   ANA     MARIA";

        var result = PersonDataExtractor.ExtractAll(body);

        Assert.Equal(2, result.Count);
        Assert.Equal("MATIAS JORGE CARVAJAL LUCERO", result[0].FullName);
        Assert.Equal("ANA MARIA SOTO ROJAS", result[1].FullName);
    }

    [Fact]
    public void Extract_NoRut_ReturnsBothNull()
    {
        var body = "CORREO EXTERNO : No haga clic. Estimados, adjunto la solicitud en el archivo. Saludos Cordiales.";

        var result = PersonDataExtractor.Extract(body);

        Assert.Null(result.Rut);
        Assert.Null(result.FullName); // a name without an anchoring RUT is not trustworthy
    }

    [Fact]
    public void Extract_InvalidCheckDigit_IsRejected()
    {
        var body = "GUSTAVO PEÑA RUT: 18.785.387-6"; // wrong check digit

        var result = PersonDataExtractor.Extract(body);

        Assert.Null(result.Rut);
    }

    [Fact]
    public void Extract_InvalidFirstRutButValidSecond_UsesTheValidOne()
    {
        var body = "ref 11.111.111-0 y carpeta de GUSTAVO PEÑA CASTRO RUT 18.785.387-7";

        var result = PersonDataExtractor.Extract(body);

        Assert.Equal("18.785.387-7", result.Rut);
        Assert.Equal("GUSTAVO PEÑA CASTRO", result.FullName);
    }

    [Fact]
    public void ExtractFromSubject_ReplyPrefixAndBoilerplateBeforeName_ExtractsRutButNeverTheName()
    {
        // Real production subject (2026-07-03): request name+RUT sit in the subject line,
        // not the body. "RV:" is Outlook's forward prefix; "SOLICITUD ANTECEDENTES" is
        // boilerplate that would otherwise get swallowed into the capped name match.
        // The name is intentionally NEVER trusted from the subject (see ExtractFromSubject
        // doc comment) — a different real subject once had generic instructions captured as
        // a "name" instead of an actual person, so the name always needs manual confirmation.
        var subject = "RV: SOLICITUD ANTECEDENTES SOLANGE KATHERINE ARRIAGADA FERNÁNDEZ RUN 16.353.860-1";

        var result = PersonDataExtractor.ExtractFromSubject(subject);

        Assert.Null(result.FullName);
        Assert.Equal("16.353.860-1", result.Rut);
    }

    [Fact]
    public void ExtractFromSubject_NoReplyPrefix_StillExtractsRutOnly()
    {
        var subject = "SOLICITUD ANTECEDENTES CARLOS ANDRADE SILVA RUT 18.785.387-7";

        var result = PersonDataExtractor.ExtractFromSubject(subject);

        Assert.Null(result.FullName);
        Assert.Equal("18.785.387-7", result.Rut);
    }

    [Fact]
    public void ExtractFromSubject_MultipleChainedPrefixes_AreAllStripped()
    {
        var subject = "RV: FW: SOLICITUD CARPETA CARLOS ANDRADE SILVA RUT 18.785.387-7";

        var result = PersonDataExtractor.ExtractFromSubject(subject);

        Assert.Null(result.FullName);
        Assert.Equal("18.785.387-7", result.Rut);
    }

    [Fact]
    public void ExtractFromSubject_UnpredictableBoilerplateNotInStripList_NeverLeaksIntoName()
    {
        // Real production failure (2026-07-07): no person name in the subject at all, just an
        // instruction phrase before the RUT. None of these words are in the strip list, but the
        // name must still come back null rather than capturing the instruction as a "name".
        var subject = "Fwd: SUBIR CARPETA PLATAFORMA CONASET 14.148.466-4";

        var result = PersonDataExtractor.ExtractFromSubject(subject);

        Assert.Null(result.FullName);
        Assert.Equal("14.148.466-4", result.Rut);
    }

    [Fact]
    public void ExtractFromSubject_NoRut_ReturnsBothNull()
    {
        var subject = "RV: Solicitud de carpeta";

        var result = PersonDataExtractor.ExtractFromSubject(subject);

        Assert.Null(result.Rut);
        Assert.Null(result.FullName);
    }

    [Fact]
    public void ExtractAll_TwoContributorsListedOnSeparateLines_ExtractsBoth()
    {
        // Real production body (2026-07-06): two contributors requested in the same email,
        // each on its own line as "NOMBRE RUT".
        var body = """
            Estimados, junto con saludar y conforme a lo establecido en el artículo 14 del Decreto 170 del MTT "REGLAMENTO PARA OTORGAMIENTO DE LICENCIA DE CONDUCIR", solicito, tenga a bien, hacer llegar a través de la PLATAFORMA SGL, carpeta con los antecedentes de conductor que se indica para la correspondiente emisión de licencia de conducir de

            EDGARD ORLANDO PACHECO CARRASCO 18.552.843-K
            JUAN CARLOS LORENZO PATIÑO GAMONAL 15.409.979-4

            Saludos cordiales,
            """;

        var result = PersonDataExtractor.ExtractAll(body);

        Assert.Equal(2, result.Count);
        Assert.Equal("EDGARD ORLANDO PACHECO CARRASCO", result[0].FullName);
        Assert.Equal("18.552.843-K", result[0].Rut);
        Assert.Equal("JUAN CARLOS LORENZO PATIÑO GAMONAL", result[1].FullName);
        Assert.Equal("15.409.979-4", result[1].Rut);
    }

    [Fact]
    public void ExtractAll_SingleContributor_ReturnsOneEntry()
    {
        var body = "Se solicita la carpeta de GUSTAVO ANDRÉS PEÑA CASTRO RUT: 18.785.387-7 por cambio de domicilio.";

        var result = PersonDataExtractor.ExtractAll(body);

        var single = Assert.Single(result);
        Assert.Equal("GUSTAVO ANDRÉS PEÑA CASTRO", single.FullName);
        Assert.Equal("18.785.387-7", single.Rut);
    }

    [Fact]
    public void ExtractAll_NoRut_ReturnsEmptyList()
    {
        var body = "CORREO EXTERNO : No haga clic. Estimados, adjunto la solicitud en el archivo. Saludos Cordiales.";

        var result = PersonDataExtractor.ExtractAll(body);

        Assert.Empty(result);
    }

    [Fact]
    public void ExtractAll_ThreeContributors_ExtractsAllInOrderWithoutBleed()
    {
        var body = """
            Solicito carpetas de los siguientes contribuyentes:

            ANA MARIA SOTO ROJAS 12.345.678-5
            PEDRO PABLO GOMEZ DIAZ 9.876.543-3
            CARLA BEATRIZ LOPEZ VEGA 15.409.979-4
            """;

        var result = PersonDataExtractor.ExtractAll(body);

        Assert.Equal(3, result.Count);
        Assert.Equal("ANA MARIA SOTO ROJAS", result[0].FullName);
        Assert.Equal("PEDRO PABLO GOMEZ DIAZ", result[1].FullName);
        Assert.Equal("CARLA BEATRIZ LOPEZ VEGA", result[2].FullName);
    }
}
