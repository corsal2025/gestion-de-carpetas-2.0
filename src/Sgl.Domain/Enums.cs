namespace Sgl.Domain;

public enum Sede
{
    AvArgentina = 1,
    Placilla = 2,
    MercadoPuerto = 3,
}

public enum EstadoCarpeta
{
    Citada = 1,
    Revision = 2,
    Alertada = 3,
    SubidaConaset = 4,
    PrimeraLicencia = 5,
    CambioDomicilio = 6,
    EsperaExamen = 7,
    ClasePendiente = 8,
    Otorgado = 9,
    ParaDenegar = 10,
    Denegado = 11,
    SubidaConF8 = 12,
    CambioDomSubidoConaset = 13,
    CambioDomSubidoCorreo = 14,
    SubidaConOficio = 15,
    SeEncuentraEnArchivos = 16,
    SeEncuentraEnOf43 = 17,
    CambioDomicilioSolicitado = 18,
    NoExisteCarpeta = 19,
    CrearCertificado = 20,
    CanjeLicExtranjera = 21,
}

public enum Decision
{
    Pendiente = 0,
    Otorgado = 1,
    Denegado = 2,
}

