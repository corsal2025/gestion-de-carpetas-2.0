using Microsoft.EntityFrameworkCore;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;
using System.Text;

if (args.Length != 1) throw new ArgumentException("Indique una carpeta nueva para datos DEMO.");
var root = Path.GetFullPath(args[0]);
if (Directory.Exists(root)) throw new IOException("La carpeta ya existe; use una nueva. No se reemplazan datos.");
Directory.CreateDirectory(root);
var dbPath = Path.Combine(root, "demo.db");
if (File.Exists(dbPath)) throw new Exception("Demo ya existe; no se reemplaza.");
using var db = new SglDbContext(new DbContextOptionsBuilder<SglDbContext>().UseSqlite($"Data Source={dbPath}").Options);
db.Database.Migrate();
var now = DateTime.UtcNow;
var today = HoraChile.Fecha(now);
var n = 24000000;
string RutDemo() {
 var body = n++; var sum=0; var f=2;
 for(var k=body;k>0;k/=10){sum+=k%10*f;f=f==7?2:f+1;}
 var dv=11-sum%11; return $"{body}-{(dv==11?"0":dv==10?"K":dv.ToString())}";
}
Ciudadano Person(string label) => new(Rut.Parse(RutDemo()), "DEMO", label);
Carpeta Agenda(string label, int days=0) {
 var c=Carpeta.CrearDesdeAgenda(Person(label),Sede.AvArgentina,today.AddDays(days),new TimeOnly(9,0),TramiteAgenda.Renovacion,"DEMO",now,now.AddDays(-40),"demo@example.invalid","000000000");
 db.Carpetas.Add(c); return c;
}
Carpeta Clasificada(string label, EstadoCarpeta state, string? date=null, int days=0) {
 var c=Agenda(label,days); c.Clasificar(state,ClaseLicencia.B,date,state==EstadoCarpeta.CambioDomicilio?"QUILPUE":null,"DEMO",now); return c;
}
Carpeta Entrante(string label,string? date=null,int days=0) {
 var c=Carpeta.RegistrarDesdeCorreo(Person(label),Sede.AvArgentina,today.AddDays(days),"QUILPUE",false,"DEMO correo simulado",now);
 c.ConfirmarReclasificacion("DEMO",now);
 if(date!=null)c.ClasificarEntrante(date,"DEMO",now);
 c.RegistrarEvento("Demo","Simulación","Solicitud ficticia recibida por correo; no se conectó buzón real","DEMO",now);
 db.Carpetas.Add(c); db.CorreosCambioDomicilio.Add(new CorreoCambioDomicilio($"<demo-{Guid.NewGuid()}@example.invalid>",c.Ciudadano.Rut,c.Id,now));return c;
}
var pending=Agenda("AGENDA POR CLASIFICAR"); pending.CompletarClasesAgenda(ClaseLicencia.B,"DEMO",now);
Clasificada("ARCHIVOS POR BUSCAR",EstadoCarpeta.SeEncuentraEnArchivos,"2022-04-10");
Clasificada("OFICINA CUARENTA Y TRES",EstadoCarpeta.SeEncuentraEnOf43,"2025-03-15");
var found=Clasificada("ARCHIVO ENCONTRADO SUBIDO",EstadoCarpeta.SeEncuentraEnArchivos,"2022-02-10");found.CambiarEstado(EstadoCarpeta.SubidaConaset,"DEMO",now);
var missing=Clasificada("F8 PENDIENTE",EstadoCarpeta.NoExisteCarpeta,"2021-03-10");
db.SolicitudesF8.Add(SolicitudF8.Solicitar(missing.Id,missing.Estado,"DEMO",now));
missing.AsignarFechaPenultimaCarpeta(today.AddYears(-8),"DEMO",now);missing.AgregarRespaldoPenultima("DEMO",now);
var cert=Clasificada("CERTIFICADO POR FIRMAR",EstadoCarpeta.NoExisteCarpeta,"2020-02-20");
var f8=SolicitudF8.Solicitar(cert.Id,cert.Estado,"DEMO",now);f8.MarcarNoSeGenero("DEMO",now,"Simulación: F8 no disponible");db.SolicitudesF8.Add(f8);
cert.ForzarEstado(EstadoCarpeta.CrearCertificado,"DEMO",now);db.SolicitudesCertificado.Add(SolicitudCertificado.CrearDesdeF8(cert.Id,f8.Id,"DEMO",now));
var signed=Clasificada("CERTIFICADO FIRMADO SIN SUBIR",EstadoCarpeta.NoExisteCarpeta,"2020-03-10");
var sf=SolicitudF8.Solicitar(signed.Id,signed.Estado,"DEMO",now);sf.MarcarNoSeGenero("DEMO",now,"Simulación");db.SolicitudesF8.Add(sf);
signed.ForzarEstado(EstadoCarpeta.CrearCertificado,"DEMO",now);var sc=SolicitudCertificado.CrearDesdeF8(signed.Id,sf.Id,"DEMO",now);sc.MarcarFirmado("DEMO",now);signed.RegistrarEvento($"CertFirma:{sc.Id}",null,signed.Estado.ToString(),"DEMO",now);db.SolicitudesCertificado.Add(sc);
var ask=Clasificada("PEDIMOS POR REVISAR",EstadoCarpeta.CambioDomicilio,null,-2);ask.ConfirmarReclasificacion("DEMO",now);
foreach(var (label,days) in new[]{("PEDIMOS EN PLAZO",-3),("PEDIMOS VENCIDO PANTALLAZO",-40)}){
 var c=Clasificada(label,EstadoCarpeta.CambioDomicilio,null,days);c.ConfirmarReclasificacion("DEMO",now);c.MarcarRevisada("DEMO",now);c.MarcarPedidaPorCorreo("QUILPUE","DEMO simulado sin envío",now.AddDays(days));
}
var uploaded=Clasificada("PEDIMOS SUBIDO POR COMUNA",EstadoCarpeta.CambioDomicilio,null,-15);uploaded.ConfirmarReclasificacion("DEMO",now);uploaded.MarcarPedidaPorCorreo("QUILPUE","DEMO",now.AddDays(-15));uploaded.MarcarSubidaPorLaComuna("DEMO",now);
Entrante("NOS PIDEN ARCHIVOS","2022-01-10",-3);
Entrante("NOS PIDEN OFICINA CUARENTA Y TRES","2025-01-10",-4);
Entrante("NOS PIDEN DIGITAL","2026-08-10",-3);
Entrante("NOS PIDEN VENCIDA SIN FECHA",null,-30);
var incomingMissing=Entrante("NOS PIDEN F8","2020-10-10",-5);incomingMissing.MarcarNoEncontradaEntrante("DEMO",now);db.SolicitudesF8.Add(SolicitudF8.Solicitar(incomingMissing.Id,incomingMissing.Estado,"DEMO",now,SentidoCambioDomicilio.Entrante));
var cancel=Agenda("CITA ANTERIOR CANCELADA",-2);cancel.RegistrarEvento("CitaCancelada","DEMO", "DEMO: cancelación recibida; carpeta conservada para revisar","DEMO",now);var newAppointment=Carpeta.CrearDesdeAgenda(cancel.Ciudadano,Sede.AvArgentina,today.AddDays(2),new TimeOnly(10,0),TramiteAgenda.CambioDomicilio,"DEMO",now,now,"demo@example.invalid");db.Carpetas.Add(newAppointment);
db.SaveChanges();
if(db.Carpetas.Count()!=18 || db.SolicitudesF8.Count()!=4 || db.SolicitudesCertificado.Count()!=2)throw new Exception("Conteos demo inesperados");
File.WriteAllText(Path.Combine(root,"comunas.csv"),"Comuna,ContactEmail,Domain\nQUILPUE,licencias@example.invalid,example.invalid\n",Encoding.UTF8);
File.WriteAllText(Path.Combine(root,"agenda-demo.csv"),"Fecha Toma Cita,Fecha Cita,Hora Inicio,RUT,Nombre,Trámite,Ubicación,Email,Telefono,Clase\n"+$"2026-10-30 09:00,{today.AddDays(1):dd-MM-yyyy},10:00,{RutDemo()},DEMO NUEVA CITA,CAMBIO DOMICILIO,AV ARGENTINA,demo@example.invalid,000000000,B\n",Encoding.UTF8);
Console.WriteLine($"DEMO LISTA: {db.Carpetas.Count()} carpetas, {db.SolicitudesF8.Count()} F8, {db.SolicitudesCertificado.Count()} certificados, {db.CorreosCambioDomicilio.Count()} solicitudes de otras comunas. SOLO FICTICIO.");
var config = new Dictionary<string, object?>
{
 ["Database"] = new { Provider = "Sqlite" },
 ["ConnectionStrings"] = new { Sgl = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = dbPath }.ToString() },
 ["Seguridad"] = new { RequerirAutenticacion = false },
 ["Certificados"] = new { EnvioCorreoActivo = false },
 ["CambioDomicilio"] = new { EnvioReal = false, PlazoSalienteDias = 15, PlazoEntranteDias = 12, CarpetaPantallazos = Path.Combine(root,"pantallazos") },
 ["Peticion"] = new { RespaldoAutomaticoActivo = false, BackupDirectory = Path.Combine(root,"backups"), ComunaDirectoryCsvPath = Path.Combine(root,"comunas.csv"), TestModeEmail = "demo@example.invalid", MailboxAddress = "demo@example.invalid", DominiosPermitidos = new[]{"example.invalid"}, Ews = new { Url = "https://127.0.0.1:1/EWS/Exchange.asmx" } },
 ["AllowedHosts"] = "localhost;127.0.0.1",
 ["Logging"] = new { LogLevel = new { Default = "Warning" } }
};
File.WriteAllText(Path.Combine(root,"appsettings.json"),System.Text.Json.JsonSerializer.Serialize(config,new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
Directory.CreateDirectory(Path.Combine(root,"appdata"));
