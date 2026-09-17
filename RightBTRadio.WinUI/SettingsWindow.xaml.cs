using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RightBTRadio.WinUI;

/// <summary>Una fila de cualquiera de las dos listas.</summary>
/// <param name="Kind">«Interno» o «Externo». Informativo: no interviene en la prioridad.</param>
/// <param name="Description">Estado del dispositivo como lo describe el Administrador de dispositivos.</param>
public sealed record RadioRow(
    string Position,
    string Primary,
    string Secondary,
    string Kind,
    string State,
    string Description,
    bool Connected,
    string HardwareId)
{
    // Mismos colores que el indicador de conexión de RightKeyboard.
    private static readonly SolidColorBrush ConnectedBrush =
        new(Windows.UI.Color.FromArgb(255, 16, 124, 65));

    private static readonly SolidColorBrush DisconnectedBrush =
        new(Windows.UI.Color.FromArgb(255, 117, 117, 117));

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

        AboutDescriptionText.Text = SettingsVisualContract.Subtitle;
        AboutVersionText.Text = $"v{ApplicationVersion()}";

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        TitleBarInteraction.Track(this, TitleBarArea, TitleBarCluster, SettingsButton, AboutButton, HelpButton);
        SetWindowIcon();
        TryEnableBackdrop();
        ResizeForCurrentDpi();

        DevicesList.ItemsSource = devices;
        PriorityList.ItemsSource = priority;

        ApplyAutomationIds();
        Reload(selectedHardwareId: null);
        ApplyTheme(configuration.Theme);
        // Con «Sistema», un cambio de tema de Windows tiene que llegar también a los botones.
        ((FrameworkElement)Content).ActualThemeChanged += (_, _) => ApplyTheme(configuration.Theme);
        StartWatching();
    }

    /// <summary>Versión del ensamblado sin el sufijo de commit que agrega el SDK.</summary>
    private static string ApplicationVersion()
    {
        string version = typeof(SettingsWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        int metadata = version.IndexOf('+');
        return metadata >= 0 ? version[..metadata] : version;
    }

    /// <summary>
    /// WinUI 3 no tiene un tema de aplicación modificable en ejecución: se aplica a la raíz
    /// de la ventana, y Mica lo sigue. Los botones de minimizar, maximizar y cerrar los
    /// dibuja el sistema y no lo siguen, así que sus colores se fijan aparte.
    /// </summary>
    private void ApplyTheme(ThemePreference preference)
    {
        if (Content is not FrameworkElement root)
        {
            return;
        }

        root.RequestedTheme = preference switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        if (AppWindowTitleBar.IsCustomizationSupported() && AppWindow?.TitleBar is { } titleBar)
        {
            bool dark = root.ActualTheme == ElementTheme.Dark;
            Windows.UI.Color foreground = dark ? Colors.White : Colors.Black;
            Windows.UI.Color hover = dark
                ? Windows.UI.Color.FromArgb(255, 50, 50, 50)
                : Windows.UI.Color.FromArgb(255, 230, 230, 230);

            // Transparente: el fondo de la franja es Mica y un color sólido lo cortaría.
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonHoverBackgroundColor = hover;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor = hover;
            titleBar.ButtonPressedForegroundColor = foreground;
        }
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (loading || ThemeRadioButtons.SelectedItem is not RadioButton { Tag: string tag } ||
            !Enum.TryParse(tag, out ThemePreference preference) ||
            // RadioButtons avisa la selección que Reload fija de forma diferida, ya fuera del
            // bloque loading; sin esta comparación la ventana guardaba al abrirse.
            preference == configuration.Theme)
        {
            return;
        }

        configuration.Theme = preference;
        ApplyTheme(preference);
        Save(SelectedDeviceHardwareId);
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
        AutomationProperties.SetAutomationId(SettingsButton, SettingsVisualContract.SettingsButtonId);
        AutomationProperties.SetAutomationId(AboutButton, SettingsVisualContract.AboutButtonId);
        AutomationProperties.SetAutomationId(HelpButton, SettingsVisualContract.HelpButtonId);
        AutomationProperties.SetAutomationId(ThemeRadioButtons, SettingsVisualContract.ThemeRadioButtonsId);
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
            configuration = Configuration.LoadOrDefault(out _);
            radios = BluetoothRadios.Enumerate();
            lastSignature = Signature(radios);
            List<ConfiguredDevice> configured = configuration.Devices;

            // La lista muestra los radios detectados y, además, los que están en el grupo
            // pero no se detectan ahora. Sin esos últimos el indicador de conexión no
            // tendría nada que informar: todo lo enumerado está conectado por definición.
            devices.Clear();
            foreach (BluetoothRadio radio in radios)
            {
                devices.Add(new RadioRow(
                    string.Empty,
                    DisplayName(configured, radio),
                    radio.HardwareId,
                    Kind(radio),
                    State(radio),
                    DeviceStatusText.Describe(radio.Problem),
                    Connected: true,
                    radio.HardwareId));
            }

            foreach (ConfiguredDevice device in configured.Where(device => FindRadio(device.HardwareId) is null))
            {
                devices.Add(MissingRow(device, string.Empty));
            }

            priority.Clear();
            for (int index = 0; index < configured.Count; index++)
            {
                ConfiguredDevice device = configured[index];
                string position = (index + 1).ToString();
                priority.Add(FindRadio(device.HardwareId) is BluetoothRadio radio
                    ? new RadioRow(
                        position,
                        DisplayName(configured, radio),
                        radio.Name,
                        Kind(radio),
                        State(radio),
                        DeviceStatusText.Describe(radio.Problem),
                        Connected: true,
                        device.HardwareId)
                    : MissingRow(device, position));
            }

            StartWithWindowsToggle.IsOn = StartupManager.IsEnabled;
            StartMinimizedToggle.IsOn = configuration.StartMinimized;
            ThemeRadioButtons.SelectedIndex = (int)configuration.Theme;

            Select(DevicesList, devices, selectedHardwareId);
            Select(PriorityList, priority, selectedHardwareId);
        }
        finally
        {
            loading = false;
        }

        UpdateButtons();
    }

    /// <summary>
    /// Un radio se llama igual esté conectado o no: el nombre que le puso el usuario, y
    /// si no le puso ninguno, el que informa Windows.
    /// </summary>
    private static string DisplayName(IEnumerable<ConfiguredDevice> configured, BluetoothRadio radio) =>
        configured.FirstOrDefault(device => Matches(device, radio.HardwareId)) is { Alias.Length: > 0 } named
            ? named.Alias
            : radio.Name;

    private static string DisplayName(ConfiguredDevice device) =>
        device.Alias.Length > 0 ? device.Alias : device.HardwareId;

    /// <summary>Fila de un radio del grupo que no se detecta ahora.</summary>
    private static RadioRow MissingRow(ConfiguredDevice device, string position) => new(
        position,
        DisplayName(device),
        device.HardwareId,
        "—",
        "—",
        "Está en el grupo, pero no está conectado.",
        Connected: false,
        device.HardwareId);

    private static string State(BluetoothRadio radio) => radio switch
    {
        { Enabled: false } => "Deshabilitado",
        { HasProblem: true } => $"Con error (código {radio.Problem})",
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
            configuration.Devices.Any(device => Matches(device, hardwareId));

        AddButton.IsEnabled = hardwareId is not null;
        AddButtonText.Text = inGroup ? "Guardar nombre" : "Agregar";
        AutomationProperties.SetName(AddButton, inGroup ? "Guardar nombre" : "Agregar al grupo");
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
            : configuration.Devices.FirstOrDefault(candidate => Matches(candidate, hardwareId));
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

        List<ConfiguredDevice> group = configuration.Devices;
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

        configuration.Devices.RemoveAll(device => Matches(device, hardwareId));
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
        List<ConfiguredDevice> group = configuration.Devices;
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
