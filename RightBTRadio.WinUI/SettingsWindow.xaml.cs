using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RightBTRadio.WinUI;

/// <summary>Una fila de cualquiera de las dos listas.</summary>
public sealed class RadioRow(string position, string primary, string secondary, string state, string hardwareId)
{
    public string Position { get; } = position;

    public string Primary { get; } = primary;

    public string Secondary { get; } = secondary;

    public string State { get; } = state;

    public string HardwareId { get; } = hardwareId;
}

public sealed partial class SettingsWindow : Window
{
    private readonly ObservableCollection<RadioRow> devices = [];
    private readonly ObservableCollection<RadioRow> priority = [];
    private Configuration configuration = new();
    private IReadOnlyList<BluetoothRadio> radios = [];
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
        TryEnableBackdrop();
        ResizeForCurrentDpi();

        DevicesList.ItemsSource = devices;
        PriorityList.ItemsSource = priority;

        ApplyAutomationIds();
        Reload(selectedHardwareId: null);
    }

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
            List<ConfiguredDevice> configured = configuration.DefaultGroup.Devices;

            devices.Clear();
            foreach (BluetoothRadio radio in radios)
            {
                bool inGroup = configured.Any(device => Matches(device, radio.HardwareId));
                devices.Add(new RadioRow(
                    string.Empty,
                    radio.Name,
                    radio.HardwareId,
                    inGroup ? State(radio) : "Sin asignar",
                    radio.HardwareId));
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
                    radio is null ? "Ausente" : State(radio),
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
            group.Add(device);
        }

        device.Alias = AliasTextBox.Text.Trim();
        device.InstanceId = FindRadio(hardwareId)?.InstanceId ?? device.InstanceId;
        Save(hardwareId);
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
