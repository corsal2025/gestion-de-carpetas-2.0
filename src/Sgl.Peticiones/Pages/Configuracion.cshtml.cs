using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Data;
using PeticionCambioDomicilio.Domain;
using PeticionCambioDomicilio.Ews;

namespace PeticionCambioDomicilio.Pages;

/// <summary>Un punto de la verificacion: verde = listo, rojo = falta algo, gris = informativo.</summary>
public sealed record Chequeo(string Nombre, bool Ok, string Detalle, bool Bloqueante = true);

public sealed class ConfiguracionModel : PageModel
{
    private readonly AppOptions _options;
    private readonly IMailSender _mail;
    private readonly ComunaDirectory _directory;
    private readonly PeticionRepository _repository;

    public ConfiguracionModel(
        AppOptions options,
        IMailSender mail,
        ComunaDirectory directory,
        PeticionRepository repository)
    {
        _options = options;
        _mail = mail;
        _directory = directory;
        _repository = repository;
    }

    public AppOptions Options => _options;
    public bool MailListo => _mail.IsConfigured;
    public bool ExcelExiste => !string.IsNullOrWhiteSpace(_options.ExcelPath) && System.IO.File.Exists(_options.ExcelPath);
    public int ComunasCount => _directory.Count;
    public string? ModoPruebaEmail => string.IsNullOrWhiteSpace(_options.TestModeEmail) ? null : _options.TestModeEmail;

    public IReadOnlyList<Chequeo> Chequeos { get; private set; } = Array.Empty<Chequeo>();
    public bool TodoListo => Chequeos.Where(c => c.Bloqueante).All(c => c.Ok);

    /// <summary>El correo EXACTO que recibiria una comuna, con datos de una peticion real
    /// (o de ejemplo si no hay ninguna cargada). Para corroborar el contenido antes de enviar.</summary>
    public string VistaPreviaAsunto { get; private set; } = "";
    public string VistaPreviaCuerpo { get; private set; } = "";
    public string VistaPreviaComuna { get; private set; } = "";
    public string? VistaPreviaDestino { get; private set; }

    public void OnGet()
    {
        Verificar();
        ArmarVistaPrevia();
    }

    private void ArmarVistaPrevia()
    {
        var real = _repository.All().FirstOrDefault(p => p.Estado != EstadoPeticion.Enviada)
                   ?? _repository.All().FirstOrDefault();

        var muestra = real ?? new Peticion
        {
            NombreCompleto = "JUAN PEREZ GONZALEZ",
            Rut = "12.345.678-5",
            Comuna = "VINA DEL MAR",
        };

        VistaPreviaAsunto = EmailTemplate.Subject();
        VistaPreviaCuerpo = EmailTemplate.Body(muestra, _options.MailboxAddress);
        VistaPreviaComuna = muestra.Comuna;
        VistaPreviaDestino = _directory.EmailsFor(muestra.Comuna) is { Count: > 0 } dir
            ? string.Join(", ", dir)
            : null;
    }

    /// <summary>Manda un correo de prueba real por EWS. Un solo intento, para no sumar al bloqueo
    /// de la cuenta si las credenciales estuvieran mal.</summary>
    public async Task<IActionResult> OnPostProbarCorreo()
    {
        if (!_mail.IsConfigured)
        {
            TempData["Flash"] = "El correo no esta configurado: falta Peticion:Ews en appsettings.Local.json.";
            return RedirectToPage();
        }

        var destino = ModoPruebaEmail ?? _options.MailboxAddress;

        var real = _repository.All().FirstOrDefault(p => p.Estado != EstadoPeticion.Enviada)
                   ?? _repository.All().FirstOrDefault();
        var muestra = real ?? new Peticion
        {
            NombreCompleto = "JUAN PEREZ GONZALEZ",
            Rut = "12.345.678-5",
            Comuna = "VINA DEL MAR",
        };

        // El asunto y cuerpo son EXACTAMENTE los que recibiria la comuna; solo se antepone [PRUEBA].
        try
        {
            await _mail.SendAsync(
                destino,
                "[PRUEBA] " + EmailTemplate.Subject(),
                "*** ESTE ES EL CORREO QUE RECIBIRIA LA COMUNA DE " + muestra.Comuna + " ***" +
                    Environment.NewLine + new string('-', 60) + Environment.NewLine + Environment.NewLine +
                    EmailTemplate.Body(muestra, _options.MailboxAddress),
                HttpContext.RequestAborted);
            TempData["Flash"] = $"OK: correo de prueba enviado a {destino}. Es identico al que recibiria la comuna (con [PRUEBA] adelante).";
        }
        catch (Exception ex)
        {
            TempData["Flash"] = $"FALLO el envio: {ex.Message}";
        }

        return RedirectToPage();
    }

    private void Verificar()
    {
        var peticiones = _repository.All();
        var sinComuna = peticiones.Count(p => p.Estado == EstadoPeticion.SinCorreoComuna);
        var enviadas = peticiones.Count(p => p.Estado == EstadoPeticion.Enviada);
        var conError = peticiones.Count(p => p.Estado == EstadoPeticion.Error);

        Chequeos = new List<Chequeo>
        {
            new("Excel de solicitudes",
                ExcelExiste,
                ExcelExiste
                    ? $"{System.IO.Path.GetFileName(_options.ExcelPath)} — última modificación {System.IO.File.GetLastWriteTime(_options.ExcelPath!):dd-MM-yyyy HH:mm}"
                    : "No se encuentra el archivo. Revisar Peticion:ExcelPath."),

            new("Directorio de comunas",
                ComunasCount > 0,
                ComunasCount > 0
                    ? $"{ComunasCount} correos municipales cargados"
                    : "Vacio. Importar desde el Excel en la pantalla Comunas."),

            new("Correo institucional (EWS)",
                MailListo,
                MailListo
                    ? $"Usuario {_options.Ews?.Username}"
                    : "Sin configurar. Falta Peticion:Ews (Url/Username/Password)."),

            new("Peticiones cargadas",
                peticiones.Count > 0,
                peticiones.Count > 0
                    ? $"{peticiones.Count} en total - {enviadas} enviadas, {conError} con error, {sinComuna} sin correo de comuna"
                    : "Ninguna. Apretar 'Cargar cambios de domicilio' en Peticiones."),

            new("Modo prueba",
                true,
                ModoPruebaEmail is not null
                    ? $"ACTIVO - todo se desvia a {ModoPruebaEmail}. Nada llega a las municipalidades."
                    : "APAGADO - los correos salen a las municipalidades reales.",
                Bloqueante: false),

            new("Comunas sin correo",
                sinComuna == 0,
                sinComuna == 0
                    ? "Todas las peticiones tienen destino"
                    : $"{sinComuna} peticion(es) sin correo de comuna. Agregar esas comunas en la pantalla Comunas.",
                Bloqueante: false),
        };
    }
}
