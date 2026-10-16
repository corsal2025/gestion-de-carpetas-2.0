using System.Runtime.InteropServices;

namespace PeticionCambioDomicilio;

/// <summary>
/// El .exe se compila como WinExe (sin consola) para que el servidor no muestre ventana ni
/// aparezca en la barra de tareas. Los modos de línea de comandos (--import, --import-comunas)
/// sí necesitan escribir: se reenganchan a la consola del proceso padre.
/// </summary>
internal static class ConsoleAttach
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    public static void ToParentIfAny()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            AttachConsole(AttachParentProcess);
        }
        catch (DllNotFoundException)
        {
            // Sin consola disponible: los Console.WriteLine simplemente no se ven.
        }
    }
}
