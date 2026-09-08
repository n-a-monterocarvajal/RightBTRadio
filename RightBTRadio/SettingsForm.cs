namespace RightBTRadio;

/// <summary>
/// Ventana de ajustes: dispositivos detectados, orden de prioridad y ajustes generales.
/// Usa la capa Fluent de RightKeyboard sobre WinForms.
/// </summary>
/// <remarks>
/// La navegación es una lista lateral, no un <see cref="TabControl"/>. WinForms no deja
/// pintar la tira de pestañas sin dibujarla a mano, así que en tema oscuro queda clara
/// sobre fondo oscuro. La lista lateral usa controles que la paleta ya cubre y además
/// es el patrón de navegación de Fluent.
/// </remarks>
internal sealed class SettingsForm : FluentForm
{
    private readonly Configuration configuration;
    private readonly ListView devicesList = NewListView();
    private readonly TextBox aliasBox = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    private readonly Button addButton = new() { Text = "Agregar al grupo", AutoSize = true };
    private readonly Button removeButton = new() { Text = "Quitar del grupo", AutoSize = true };
    private readonly ListView priorityList = NewListView();
    private readonly Button upButton = new() { Text = "Subir", AutoSize = true };
    private readonly Button downButton = new() { Text = "Bajar", AutoSize = true };
    private readonly CheckBox startWithWindows = new() { Text = "Iniciar con Windows", AutoSize = true };
    private readonly CheckBox startMinimized = new() { Text = "Iniciar minimizado en la bandeja", AutoSize = true };
    private readonly Panel content = new() { Dock = DockStyle.Fill };
    private IReadOnlyList<BluetoothRadio> radios = [];
    private bool loading;

    public SettingsForm(Configuration configuration)
        : base(FluentBackdropKind.Main)
    {
        this.configuration = configuration;

        Text = "RightBTRadio";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(840, 500);
        MinimumSize = new Size(720, 460);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        Panel devicesPage = BuildDevicesPage();
        Panel priorityPage = BuildPriorityPage();
        Panel generalPage = BuildGeneralPage();
        content.Controls.Add(devicesPage);
        content.Controls.Add(priorityPage);
        content.Controls.Add(generalPage);

        FlowLayoutPanel navigation = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 12, 0)
        };
        navigation.Controls.Add(NewNavigationItem("Dispositivos", devicesPage, selected: true));
        navigation.Controls.Add(NewNavigationItem("Prioridad", priorityPage, selected: false));
        navigation.Controls.Add(NewNavigationItem("Ajustes generales", generalPage, selected: false));

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(navigation, 0, 0);
        root.Controls.Add(content, 1, 0);
        Controls.Add(root);

        devicesList.Resize += (_, _) => FitColumns(devicesList, 0.30, 0.48, 0.22);
        priorityList.Resize += (_, _) => FitColumns(priorityList, 0.30, 0.46, 0.24);
        devicesList.SelectedIndexChanged += (_, _) => OnDeviceSelected();
        addButton.Click += (_, _) => AddOrUpdateSelectedDevice();
        removeButton.Click += (_, _) => RemoveSelectedDevice();
        upButton.Click += (_, _) => MoveSelected(-1);
        downButton.Click += (_, _) => MoveSelected(1);
        priorityList.SelectedIndexChanged += (_, _) => UpdateButtons();
        startWithWindows.CheckedChanged += (_, _) => OnStartWithWindowsChanged();
        startMinimized.CheckedChanged += (_, _) => OnStartMinimizedChanged();

        Reload(selectedHardwareId: null);
    }

    private static ListView NewListView() => new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        BorderStyle = BorderStyle.None
    };

    private RadioButton NewNavigationItem(string text, Panel page, bool selected)
    {
        RadioButton item = new()
        {
            Text = text,
            Appearance = Appearance.Button,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            Size = new Size(168, 36),
            Margin = new Padding(0, 0, 0, 4),
            Checked = selected
        };

        page.Visible = selected;
        item.CheckedChanged += (_, _) =>
        {
            page.Visible = item.Checked;
            if (item.Checked)
            {
                page.BringToFront();
            }
        };

        return item;
    }

    private static Label NewTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Dock = DockStyle.Top,
        Font = FluentTypography.CreateTitleFont(14f),
        Margin = new Padding(0),
        Padding = new Padding(0, 0, 0, 8)
    };

    private static FluentTableLayoutPanel NewCard(Control content)
    {
        FluentTableLayoutPanel card = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(8)
        };
        card.Controls.Add(content, 0, 0);
        return card;
    }

    private Panel BuildDevicesPage()
    {
        devicesList.Columns.Add("Nombre", 240);
        devicesList.Columns.Add("Hardware ID", 320);
        devicesList.Columns.Add("Estado", 150);

        TableLayoutPanel actions = new()
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ColumnCount = 4,
            Padding = new Padding(0, 10, 0, 0)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.Controls.Add(
            new Label
            {
                Text = "Alias:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 8, 0)
            },
            0,
            0);
        actions.Controls.Add(aliasBox, 1, 0);
        actions.Controls.Add(addButton, 2, 0);
        actions.Controls.Add(removeButton, 3, 0);

        Panel page = new() { Dock = DockStyle.Fill };
        page.Controls.Add(NewCard(devicesList));
        page.Controls.Add(actions);
        page.Controls.Add(NewTitle("Dispositivos"));
        return page;
    }

    private Panel BuildPriorityPage()
    {
        priorityList.Columns.Add("Alias", 220);
        priorityList.Columns.Add("Nombre", 260);
        priorityList.Columns.Add("Estado", 150);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 0)
        };
        actions.Controls.Add(upButton);
        actions.Controls.Add(downButton);
        Label hint = new()
        {
            Text = "El primero de la lista es el que queda habilitado.",
            AutoSize = true,
            Margin = new Padding(12, 6, 0, 0)
        };
        FluentTheme.Mark(hint, FluentThemeRole.SecondaryText);
        actions.Controls.Add(hint);

        Panel page = new() { Dock = DockStyle.Fill };
        page.Controls.Add(NewCard(priorityList));
        page.Controls.Add(actions);
        page.Controls.Add(NewTitle("Prioridad"));
        return page;
    }

    private Panel BuildGeneralPage()
    {
        FlowLayoutPanel options = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(4)
        };
        options.Controls.Add(startWithWindows);
        options.Controls.Add(startMinimized);

        Panel page = new() { Dock = DockStyle.Fill };
        page.Controls.Add(NewCard(options));
        page.Controls.Add(NewTitle("Ajustes generales"));
        return page;
    }

    private void Reload(string? selectedHardwareId)
    {
        loading = true;
        try
        {
            radios = BluetoothRadios.Enumerate();
            List<ConfiguredDevice> configured = configuration.DefaultGroup.Devices;

            devicesList.BeginUpdate();
            devicesList.Items.Clear();
            foreach (BluetoothRadio radio in radios)
            {
                bool inGroup = configured.Any(device => Matches(device, radio.HardwareId));
                ListViewItem item = new(radio.Name) { Tag = radio.HardwareId };
                item.SubItems.Add(radio.HardwareId);
                item.SubItems.Add(inGroup ? GroupedState(radio) : UngroupedState(radio));
                devicesList.Items.Add(item);
            }

            devicesList.EndUpdate();

            priorityList.BeginUpdate();
            priorityList.Items.Clear();
            foreach (ConfiguredDevice device in configured)
            {
                BluetoothRadio? radio = FindRadio(device.HardwareId);
                ListViewItem item = new(device.Alias.Length > 0 ? device.Alias : device.HardwareId)
                {
                    Tag = device.HardwareId
                };
                item.SubItems.Add(radio?.Name ?? device.HardwareId);
                item.SubItems.Add(radio is null ? "Ausente" : GroupedState(radio));
                priorityList.Items.Add(item);
            }

            priorityList.EndUpdate();

            startWithWindows.Checked = StartupManager.IsEnabled;
            startMinimized.Checked = configuration.StartMinimized;

            SelectByHardwareId(devicesList, selectedHardwareId);
            SelectByHardwareId(priorityList, selectedHardwareId);
        }
        finally
        {
            loading = false;
        }

        OnDeviceSelected();
        RefreshTheme();
    }

    /// <summary>
    /// Reparte el ancho disponible entre las columnas. Sin esto, los anchos fijos
    /// desbordan la lista y aparece una barra horizontal que corta la última columna.
    /// </summary>
    private static void FitColumns(ListView list, params double[] fractions)
    {
        int available = list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
        if (available <= 0)
        {
            return;
        }

        for (int index = 0; index < list.Columns.Count && index < fractions.Length; index++)
        {
            list.Columns[index].Width = (int)(available * fractions[index]);
        }
    }

    private static string GroupedState(BluetoothRadio radio) => radio.Enabled ? "Habilitado" : "Deshabilitado";

    private static string UngroupedState(BluetoothRadio radio) =>
        radio.Enabled ? "Sin asignar" : "Sin asignar, deshabilitado";

    private static void SelectByHardwareId(ListView list, string? hardwareId)
    {
        if (hardwareId is null)
        {
            return;
        }

        foreach (ListViewItem item in list.Items)
        {
            if (Equals(item.Tag as string, hardwareId))
            {
                item.Selected = true;
                item.EnsureVisible();
                return;
            }
        }
    }

    private static bool Matches(ConfiguredDevice device, string hardwareId) =>
        string.Equals(device.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase);

    private BluetoothRadio? FindRadio(string hardwareId) =>
        radios.FirstOrDefault(radio => string.Equals(radio.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase));

    private static string? SelectedHardwareId(ListView list) =>
        list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as string : null;

    private void OnDeviceSelected()
    {
        if (loading)
        {
            return;
        }

        string? hardwareId = SelectedHardwareId(devicesList);
        ConfiguredDevice? device = hardwareId is null
            ? null
            : configuration.DefaultGroup.Devices.FirstOrDefault(candidate => Matches(candidate, hardwareId));

        aliasBox.Text = device?.Alias ?? string.Empty;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        string? deviceHardwareId = SelectedHardwareId(devicesList);
        bool inGroup = deviceHardwareId is not null &&
            configuration.DefaultGroup.Devices.Any(device => Matches(device, deviceHardwareId));

        addButton.Enabled = deviceHardwareId is not null;
        addButton.Text = inGroup ? "Actualizar alias" : "Agregar al grupo";
        removeButton.Enabled = inGroup;

        int index = priorityList.SelectedIndices.Count == 1 ? priorityList.SelectedIndices[0] : -1;
        upButton.Enabled = index > 0;
        downButton.Enabled = index >= 0 && index < priorityList.Items.Count - 1;
    }

    private void AddOrUpdateSelectedDevice()
    {
        if (SelectedHardwareId(devicesList) is not string hardwareId)
        {
            return;
        }

        BluetoothRadio? radio = FindRadio(hardwareId);
        List<ConfiguredDevice> devices = configuration.DefaultGroup.Devices;
        ConfiguredDevice? device = devices.FirstOrDefault(candidate => Matches(candidate, hardwareId));
        if (device is null)
        {
            device = new ConfiguredDevice { HardwareId = hardwareId };
            devices.Add(device);
        }

        device.Alias = aliasBox.Text.Trim();
        device.InstanceId = radio?.InstanceId ?? device.InstanceId;
        Save(hardwareId);
    }

    private void RemoveSelectedDevice()
    {
        if (SelectedHardwareId(devicesList) is not string hardwareId)
        {
            return;
        }

        configuration.DefaultGroup.Devices.RemoveAll(device => Matches(device, hardwareId));
        Save(hardwareId);
    }

    private void MoveSelected(int offset)
    {
        if (priorityList.SelectedIndices.Count != 1)
        {
            return;
        }

        int index = priorityList.SelectedIndices[0];
        int target = index + offset;
        List<ConfiguredDevice> devices = configuration.DefaultGroup.Devices;
        if (target < 0 || target >= devices.Count)
        {
            return;
        }

        (devices[index], devices[target]) = (devices[target], devices[index]);
        Save(devices[target].HardwareId);
    }

    private void OnStartWithWindowsChanged()
    {
        if (loading)
        {
            return;
        }

        try
        {
            StartupManager.SetEnabled(startWithWindows.Checked);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException)
        {
            MessageBox.Show(
                $"No se pudo cambiar el arranque con Windows.\n\n{error.Message}",
                "RightBTRadio",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            loading = true;
            startWithWindows.Checked = StartupManager.IsEnabled;
            loading = false;
        }
    }

    private void OnStartMinimizedChanged()
    {
        if (loading)
        {
            return;
        }

        configuration.StartMinimized = startMinimized.Checked;
        Save(SelectedHardwareId(devicesList));
    }

    private void Save(string? selectedHardwareId)
    {
        try
        {
            configuration.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"No se pudo guardar la configuración.\n\n{error.Message}",
                "RightBTRadio",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        Reload(selectedHardwareId);
    }
}
