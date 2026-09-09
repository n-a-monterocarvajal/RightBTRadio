using NUnit.Framework;

namespace RightBTRadio.Tests;

[TestFixture]
public sealed class PriorityResolverTests
{
    private static PriorityGroup Group(params string[] hardwareIds) => new()
    {
        Id = Configuration.DefaultGroupId,
        Name = "Radios Bluetooth",
        Devices = hardwareIds.Select(id => new ConfiguredDevice { HardwareId = id }).ToList()
    };

    private static BluetoothRadio Radio(string hardwareId, bool enabled = true) =>
        new(hardwareId, $@"INSTANCE\{hardwareId}", hardwareId, enabled ? 0u : BluetoothRadio.ProblemDisabled, Removable: true);

    [Test]
    public void DeshabilitaLosDeMenorPrioridadYDejaElPrimero()
    {
        ResolutionPlan plan = PriorityResolver.Resolve(
            Group("EXTERNO", "INTERNO"),
            [Radio("INTERNO"), Radio("EXTERNO")]);

        Assert.That(plan.Enable, Is.Null, "el de mayor prioridad ya estaba habilitado");
        Assert.That(plan.Disable.Select(radio => radio.HardwareId), Is.EqualTo(new[] { "INTERNO" }));
    }

    [Test]
    public void HabilitaElDeMayorPrioridadCuandoEstabaDeshabilitado()
    {
        ResolutionPlan plan = PriorityResolver.Resolve(
            Group("EXTERNO", "INTERNO"),
            [Radio("EXTERNO", enabled: false), Radio("INTERNO")]);

        Assert.That(plan.Enable?.HardwareId, Is.EqualTo("EXTERNO"));
        Assert.That(plan.Disable.Select(radio => radio.HardwareId), Is.EqualTo(new[] { "INTERNO" }));
    }

    [Test]
    public void AlDesconectarseElDeMayorPrioridadGanaElSiguientePresente()
    {
        ResolutionPlan plan = PriorityResolver.Resolve(
            Group("EXTERNO", "INTERNO"),
            [Radio("INTERNO", enabled: false)]);

        Assert.That(plan.Enable?.HardwareId, Is.EqualTo("INTERNO"));
        Assert.That(plan.Disable, Is.Empty);
    }

    [Test]
    public void NoHaceNadaSiElEstadoYaEsCorrecto()
    {
        ResolutionPlan plan = PriorityResolver.Resolve(
            Group("EXTERNO", "INTERNO"),
            [Radio("EXTERNO"), Radio("INTERNO", enabled: false)]);

        Assert.That(plan.IsEmpty, Is.True);
    }

    [Test]
    public void NoTocaRadiosQueNoEstanEnElGrupo()
    {
        ResolutionPlan plan = PriorityResolver.Resolve(
            Group("EXTERNO"),
            [Radio("EXTERNO"), Radio("DESCONOCIDO")]);

        Assert.That(plan.IsEmpty, Is.True);
    }

    [Test]
    public void SinDispositivosDelGrupoPresentesNoHaceNada()
    {
        ResolutionPlan plan = PriorityResolver.Resolve(Group("EXTERNO", "INTERNO"), []);

        Assert.That(plan.IsEmpty, Is.True);
    }

    [Test]
    public void ElGanadorSeInformaAunqueNoHagaFaltaHabilitarlo()
    {
        ResolutionPlan plan = PriorityResolver.Resolve(
            Group("EXTERNO", "INTERNO"),
            [Radio("EXTERNO"), Radio("INTERNO")]);

        Assert.That(plan.Winner?.HardwareId, Is.EqualTo("EXTERNO"));
        Assert.That(plan.Enable, Is.Null);
    }

    [Test]
    public void UnGanadorConProblemaDeInstalacionSigueGanando()
    {
        // Con dos radios presentes Windows deja al segundo en CM_PROB_FAILED_INSTALL.
        // No es lo mismo que estar deshabilitado, y no debe cambiar la prioridad: el
        // problema se resuelve reenumerando el nodo una vez que el otro se deshabilita.
        BluetoothRadio conProblema = new("EXTERNO", @"INSTANCE\EXTERNO", "Externo", Problem: 31, Removable: true);

        ResolutionPlan plan = PriorityResolver.Resolve(
            Group("EXTERNO", "INTERNO"),
            [conProblema, Radio("INTERNO")]);

        Assert.That(conProblema.Enabled, Is.True);
        Assert.That(conProblema.HasProblem, Is.True);
        Assert.That(plan.Winner?.HardwareId, Is.EqualTo("EXTERNO"));
        Assert.That(plan.Enable, Is.Null, "no estaba deshabilitado, así que no hay que habilitarlo");
        Assert.That(plan.Disable.Select(radio => radio.HardwareId), Is.EqualTo(new[] { "INTERNO" }));
    }

    [Test]
    public void ConDosRadiosIdenticosElegirUnoDeFormaDeterminista()
    {
        BluetoothRadio primero = new("IGUAL", @"INSTANCE\A", "A", Problem: 0, Removable: true);
        BluetoothRadio segundo = new("IGUAL", @"INSTANCE\B", "B", Problem: 0, Removable: true);

        ResolutionPlan directo = PriorityResolver.Resolve(Group("IGUAL"), [primero, segundo]);
        ResolutionPlan inverso = PriorityResolver.Resolve(Group("IGUAL"), [segundo, primero]);

        Assert.That(directo.IsEmpty, Is.True);
        Assert.That(inverso.IsEmpty, Is.True);
    }

    [Test]
    public void ElEnumeradorBthIdentificaHijosDeUnRadio()
    {
        Assert.That(BluetoothRadios.IsChildOfARadio(@"BTHENUM\{0000110b-0000-1000-8000-00805f9b34fb}_LOCALMFG"), Is.True);
        Assert.That(BluetoothRadios.IsChildOfARadio(@"BTH\MS_BTHBRB\7&1a2b3c4d&0"), Is.True);
        Assert.That(BluetoothRadios.IsChildOfARadio(@"USB\VID_368B&PID_8D81\5&2f3a1b0c&0&2"), Is.False);
        Assert.That(BluetoothRadios.IsChildOfARadio(@"PCI\VEN_8086&DEV_A370\3&11583659&0&A3"), Is.False);
    }
}
