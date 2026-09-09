using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RightBTRadio.WinUI;

/// <summary>Una fila de cualquiera de las dos listas.</summary>
public sealed class RadioRow
{
    // Mismos colores que el indicador de conexión de RightKeyboard.
    private static readonly SolidColorBrush ConnectedBrush =
        new(Windows.UI.Color.FromArgb(255, 16, 124, 65));

    private static readonly SolidColorBrush DisconnectedBrush =
        new(Windows.UI.Color.FromArgb(255, 117, 117, 117));

    public RadioRow(
        string position,
        string primary,
        string secondary,
        string kind,
        string state,
        string description,
        bool connected,
        string hardwareId)
    {
        Position = position;
        Primary = primary;
        Secondary = secondary;
        Kind = kind;
        State = state;
        Description = description;
        Connected = connected;
        HardwareId = hardwareId;
    }

    public string Position { get; }

    public string Primary { get; }

    public string Secondary { get; }

    /// <summary>«Interno» o «Externo». Informativo: no interviene en la prioridad.</summary>
    public string Kind { get; }

    public string State { get; }

    /// <summary>Estado del dispositivo en la forma en que lo describe el Administrador de dispositivos.</summary>
    public string Description { get; }

    public bool Connected { get; }

    public string HardwareId { get; }

    public string ConnectionText => Connected ? "Conectado" : "Desconectado";

    public SolidColorBrush ConnectionBrush => Connected ? ConnectedBrush : DisconnectedBrush;
}

public sealed partial class SettingsWindow : Window
{
    private readonly ObservableCollection<RadioRow> devices = [];
    private readonly ObservableCollection<RadioRow> priority = [];
    private Configuration configuration = new();
    private IReadOnlyList<BluetoothRadio> radios = [];
    private string lastSignature = string.Empty;
    private bool loading;

    public SettingsWindow()
    {
        InitializeComponent();

        Title = SettingsVisualContract.WindowTitle;
        TitleBarText.Text = SettingsVisualContract.WindowTitle;
        Subtitle.Text = SettingsVisualContract.Subtitle;
        DevicesDescription.Text = SettingsVisualContract.DevicesDescription;
        PriorityDescription.Text = SettingsVisualContract.PriorityDescription;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        SetWindowIcon();
        TryEnableBackdrop();
        ResizeForCurrentDpi();

        DevicesList.ItemsSource = devices;
        PriorityList.ItemsSource = priority;

        ApplyAutomationIds();
        Reload(selectedHardwareId: null);
        StartWatching();
    }

    /// <summary>
    /// Mantiene la lista al día mientras la ventana está abierta. El residente sí escucha
    /// eventos; esta ventana sondea porque vive poco y en primer plano, y engancharse a
    /// <c>WM_DEVICECHANGE</c> desde WinUI exige subclasificar el HWND, más maquinaria de
    /// la que el caso justifica. Solo recarga cuando algo cambió de verdad, así que no
    /// parpadea ni pierde la selección.
    /// </summary>
    private void StartWatching()
    {
        DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += (_, _) => RefreshIfChanged();
        timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    private void RefreshIfChanged()
    {
        string current = Signature(BluetoothRadios.Enumerate());
        if (current == lastSignature)
        {
            return;
        }

        // No pisar un alias a medio escribir.
        string? selected = SelectedDeviceHardwareId;
        string draft = AliasTextBox.Text;
        bool editing = AliasTextBox.FocusState != FocusState.Unfocused;

        Reload(selected);

        if (editing)
        {
            AliasTextBox.Text = draft;
        }
    }

    private static string Signature(IReadOnlyList<BluetoothRadio> radios) =>
        string.Join('|', radios.Select(radio => $"{radio.HardwareId}:{radio.Problem}"));

    /// <remarks>
    /// Los identificadores se fijan en código, no en XAML, porque el contrato es
    /// <c>internal</c> en RightBTRadio.Core y así hay una sola fuente de verdad.
    /// </remarks>
    private void ApplyAutomationIds()
    {
        AutomationProperties.SetAutomationId(Subtitle, SettingsVisualContract.SubtitleId);
        AutomationProperties.SetAutomationId(DevicesList, SettingsVisualContract.DevicesListId);
        AutomationProperties.SetAutomationId(PriorityList, SettingsVisualContract.PriorityListId);
        AutomationProperties.SetAutomationId(AliasTextBox, SettingsVisualContract.AliasTextBoxId);
        AutomationProperties.SetAutomationId(AddButton, SettingsVisualContract.AddButtonId);
        AutomationProperties.SetAutomationId(RemoveButton, SettingsVisualContract.RemoveButtonId);
        AutomationProperties.SetAutomationId(MoveUpButton, SettingsVisualContract.MoveUpButtonId);
        AutomationProperties.SetAutomationId(MoveDownButton, SettingsVisualContract.MoveDownButtonId);
        AutomationProperties.SetAutomationId(StartWithWindowsToggle, SettingsVisualContract.StartWithWindowsToggleId);
        AutomationProperties.SetAutomationId(StartMinimizedToggle, SettingsVisualContract.StartMinimizedToggleId);
        AutomationProperties.SetAutomationId(DevicesDescription, SettingsVisualContract.DevicesDescriptionId);
        AutomationProperties.SetAutomationId(PriorityDescription, SettingsVisualContract.PriorityDescriptionId);
    }

    /// <summary>
    /// Icono de la barra de tareas y de Alt+Tab. La barra de título lo muestra aparte,
    /// desde el XAML, porque con <c>ExtendsContentIntoTitleBar</c> el marco no dibuja el
    /// suyo.
    /// </summary>
    private void SetWindowIcon()
    {
        string icon = Path.Combine(AppContext.BaseDirectory, @"Assets\RightBTRadio.ico");
        if (!File.Exists(icon))
        {
            return;
        }

        try
        {
            AppWindow.SetIcon(icon);
        }
        catch (Exception error) when (error is ArgumentException or IOException)
        {
            Log.Write($"No se pudo aplicar el icono de la ventana: {error.Message}");
        }
    }

    private void TryEnableBackdrop()
    {
        // Mica es el material que Fluent reserva para el fondo de la ventana principal.
        // WinUI cae por su cuenta a un color sólido del tema cuando no puede renderizarlo
        // (máquina virtual, escritorio remoto, transparencia desactivada, alto contraste).
        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch (Exception error)
        {
            Log.Write($"No se pudo activar Mica: {error.Message}");
            SystemBackdrop = null;
        }
    }

    private void ResizeForCurrentDpi()
    {
        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        uint dpi = Math.Max(GetDpiForWindow(handle), 96u);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)Math.Ceiling(SettingsVisualContract.MinimumWidth * dpi / 96d),
            (int)Math.Ceiling(SettingsVisualContract.MinimumHeight * dpi / 96d)));
    }

    private void Reload(string? selectedHardwareId)
    {
        loading = true;
        try
        {
            configuration = LoadConfiguration();
            radios = BluetoothRadios.Enumerate();
            lastSignature = Signature(radios);
            List<ConfiguredDevice> configured = configuration.DefaultGroup.Devices;

            // La lista muestra los radios presentes y, además, los que están en el grupo
            // pero no se detectan ahora. Sin esos últimos el indicador de conexión no
            // tendría nada que informar: todo lo enumerado está conectado por definición.
            devices.Clear();
            foreach (BluetoothRadio radio in radios)
            {
                devices.Add(new RadioRow(
                    string.Empty,
                    radio.Name,
                    radio.HardwareId,
                    Kind(radio),
                    State(radio),
                    DeviceStatusText.Describe(radio.Problem),
                    connected: true,
                    radio.HardwareId));
            }

            foreach (ConfiguredDevice device in configured.Where(device => FindRadio(device.HardwareId) is null))
            {
                devices.Add(new RadioRow(
                    string.Empty,
                    device.Alias.Length > 0 ? device.Alias : device.HardwareId,
                    device.HardwareId,
                    "—",
                    "—",
                    "Este radio está en el grupo pero no se detecta ahora.",
                    connected: false,
                    device.HardwareId));
            }

            priority.Clear();
            for (int index = 0; index < configured.Count; index++)
            {
                ConfiguredDevice device = configured[index];
                BluetoothRadio? radio = FindRadio(device.HardwareId);
                priority.Add(new RadioRow(
                    (index + 1).ToString(),
                    device.Alias.Length > 0 ? device.Alias : device.HardwareId,
                    radio?.Name ?? device.HardwareId,
                    radio is null ? "—" : Kind(radio),
                    radio is null ? "—" : State(radio),
                    radio is null
                        ? "Este radio está en el grupo pero no se detecta ahora."
                        : DeviceStatusText.Describe(radio.Problem),
                    connected: radio is not null,
                    device.HardwareId));
            }

            StartWithWindowsToggle.IsOn = StartupManager.IsEnabled;
            StartMinimizedToggle.IsOn = configuration.StartMinimized;

            Select(DevicesList, devices, selectedHardwareId);
            Select(PriorityList, priority, selectedHardwareId);
        }
        finally
        {
            loading = false;
        }

        UpdateButtons();
    }

    private static Configuration LoadConfiguration()
    {
        try
        {
            return Configuration.Load();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Log.Write($"No se pudo cargar la configuración: {error.Message}");
            return new Configuration();
        }
    }

    private static string State(BluetoothRadio radio) => radio switch
    {
        { Enabled: false } => "Deshabilitado",
        { HasProblem: true } => $"Con problema {radio.Problem}",
        _ => "Habilitado"
    };

    private static string Kind(BluetoothRadio radio) => radio.Removable ? "Externo" : "Interno";

    private static bool Matches(ConfiguredDevice device, string hardwareId) =>
        string.Equals(device.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase);

    private BluetoothRadio? FindRadio(string hardwareId) =>
        radios.FirstOrDefault(radio => string.Equals(radio.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase));

    private static void Select(ListView list, ObservableCollection<RadioRow> rows, string? hardwareId)
    {
        if (hardwareId is null)
        {
            return;
        }

        RadioRow? row = rows.FirstOrDefault(
            candidate => string.Equals(candidate.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            list.SelectedItem = row;
        }
    }

    private string? SelectedDeviceHardwareId => (DevicesList.SelectedItem as RadioRow)?.HardwareId;

    private void UpdateButtons()
    {
        string? hardwareId = SelectedDeviceHardwareId;
        bool inGroup = hardwareId is not null &&
            configuration.DefaultGroup.Devices.Any(device => Matches(device, hardwareId));

        AddButton.IsEnabled = hardwareId is not null;
        AddButton.Content = inGroup ? "Actualizar alias" : "Agregar al grupo";
        RemoveButton.IsEnabled = inGroup;

        int index = PriorityList.SelectedIndex;
        MoveUpButton.IsEnabled = index > 0;
        MoveDownButton.IsEnabled = index >= 0 && index < priority.Count - 1;
    }

    private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (loading)
        {
            return;
        }

        string? hardwareId = SelectedDeviceHardwareId;
        ConfiguredDevice? device = hardwareId is null
            ? null
            : configuration.DefaultGroup.Devices.FirstOrDefault(candidate => Matches(candidate, hardwareId));
        AliasTextBox.Text = device?.Alias ?? string.Empty;
        UpdateButtons();
    }

    private void OnPrioritySelectionChanged(object sender, SelectionChangedEventArgs args) => UpdateButtons();

    private void OnAddClick(object sender, RoutedEventArgs args)
    {
        if (SelectedDeviceHardwareId is not string hardwareId)
        {
            return;
        }

        List<ConfiguredDevice> group = configuration.DefaultGroup.Devices;
        ConfiguredDevice? device = group.FirstOrDefault(candidate => Matches(candidate, hardwareId));
        if (device is null)
        {
            device = new ConfiguredDevice { HardwareId = hardwareId };
            group.Insert(InsertionIndex(group, hardwareId), device);
        }

        device.Alias = AliasTextBox.Text.Trim();
        device.InstanceId = FindRadio(hardwareId)?.InstanceId ?? device.InstanceId;
        Save(hardwareId);
    }

    /// <summary>
    /// Dónde entra un radio que se agrega al grupo. Un radio que el usuario ya había
    /// deshabilitado por su cuenta es una declaración de preferencia, así que lo que
    /// está habilitado entra por encima de lo deshabilitado. Solo decide el orden
    /// inicial: después manda lo que el usuario ordene a mano.
    /// </summary>
    private int InsertionIndex(List<ConfiguredDevice> group, string hardwareId)
    {
        if (FindRadio(hardwareId) is not { Enabled: true })
        {
            return group.Count;
        }

        int firstDisabled = group.FindIndex(
            candidate => FindRadio(candidate.HardwareId) is { Enabled: false });
        return firstDisabled >= 0 ? firstDisabled : group.Count;
    }

    private void OnRemoveClick(object sender, RoutedEventArgs args)
    {
        if (SelectedDeviceHardwareId is not string hardwareId)
        {
            return;
        }

        configuration.DefaultGroup.Devices.RemoveAll(device => Matches(device, hardwareId));
        Save(hardwareId);
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs args) => Move(-1);

    private void OnMoveDownClick(object sender, RoutedEventArgs args) => Move(1);

    private void Move(int offset)
    {
        int index = PriorityList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        int target = index + offset;
        List<ConfiguredDevice> group = configuration.DefaultGroup.Devices;
        if (target < 0 || target >= group.Count)
        {
            return;
        }

        (group[index], group[target]) = (group[target], group[index]);
        Save(group[target].HardwareId);
    }

    private void OnStartWithWindowsToggled(object sender, RoutedEventArgs args)
    {
        if (loading)
        {
            return;
        }

        try
        {
            StartupManager.SetEnabled(StartWithWindowsToggle.IsOn);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Write($"No se pudo cambiar el arranque con Windows: {error.Message}");
            loading = true;
            StartWithWindowsToggle.IsOn = StartupManager.IsEnabled;
            loading = false;
        }
    }

    private void OnStartMinimizedToggled(object sender, RoutedEventArgs args)
    {
        if (loading)
        {
            return;
        }

        configuration.StartMinimized = StartMinimizedToggle.IsOn;
        Save(SelectedDeviceHardwareId);
    }

    private void Save(string? selectedHardwareId)
    {
        try
        {
            configuration.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Write($"No se pudo guardar la configuración: {error.Message}");
        }

        Reload(selectedHardwareId);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
}
