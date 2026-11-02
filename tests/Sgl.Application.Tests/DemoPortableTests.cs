namespace Sgl.Application.Tests;

public sealed class DemoPortableTests
{
    [Fact]
    public void Arranque_demo_aisla_secretos_y_rechaza_configuracion_local()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Sgl.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var script = File.ReadAllText(Path.Combine(root.FullName, "scripts", "demo", "Abrir-Demo.ps1"));
        Assert.Contains("$env:APPDATA =", script);
        Assert.Contains("appsettings.Local.json", script);
        Assert.Contains("--contentRoot", script);
        Assert.Contains("--webroot", script);
        Assert.Contains("127.0.0.1", script);
        Assert.DoesNotContain("Start-Process", script);
        Assert.DoesNotContain("Remove-Item", script);
    }
}
