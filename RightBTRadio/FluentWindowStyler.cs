using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace RightBTRadio;

// Capa Fluent para WinForms tomada de FluentWindowStyler de RightKeyboard. Los cambios
// respecto del original son tres: se quitó FluentContextMenuStrip, porque el menú de la
// bandeja es Win32 nativo; la paleta lleva el indicador de tema oscuro, y hay un caso
// para ListView, que RightKeyboard no usa y sin el cual queda blanco en tema oscuro.

internal enum FluentBackdropKind
{
    Main = 2,
    Transient = 3,
    Tabbed = 4
}

internal static class FluentBackdropPolicy
{
    private static readonly Version Windows11Build22000 = new(10, 0, 22000);
    private static readonly Version Windows11Build22621 = new(10, 0, 22621);

    public static bool CanUseRoundedFrame(Version windowsVersion) => windowsVersion >= Windows11Build22000;

    public static bool CanUseSystemBackdrop(Version windowsVersion, bool highContrast) =>
        !highContrast && windowsVersion >= Windows11Build22621;
}

internal static class FluentWindowStyler
{
    private const int UseImmersiveDarkMode = 20;
    private const int WindowCornerPreference = 33;
    private const int SystemBackdropType = 38;
    private const int RoundCornerPreference = 2;
    private const int NoSystemBackdrop = 1;

    public static bool Apply(nint handle, FluentBackdropKind backdrop, bool darkMode)
    {
        Version version = Environment.OSVersion.Version;
        if (!FluentBackdropPolicy.CanUseRoundedFrame(version))
        {
            return false;
        }

        int dark = darkMode ? 1 : 0;
        int corner = RoundCornerPreference;
        TrySetAttribute(handle, UseImmersiveDarkMode, dark);
        TrySetAttribute(handle, WindowCornerPreference, corner);

        if (!FluentBackdropPolicy.CanUseSystemBackdrop(version, SystemInformation.HighContrast))
        {
            TrySetAttribute(handle, SystemBackdropType, NoSystemBackdrop);
            return false;
        }

        // Las superficies GDI de WinForms no conservan de forma fiable el canal alfa
        // cuando el marco DWM se extiende sobre toda el área cliente. En tema claro
        // esto puede convertir textos y controles en superficies casi transparentes.
        // Se mantiene un fondo sólido.
        TrySetAttribute(handle, SystemBackdropType, NoSystemBackdrop);
        return false;
    }

    private static bool TrySetAttribute(nint handle, int attribute, int value)
    {
        try
        {
            return DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) >= 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int valueSize);
}

internal enum FluentThemeRole
{
    SecondaryText,
    PrimaryButton,
    Surface,
    Border
}

internal readonly record struct FluentPalette(
    bool Dark,
    Color Window,
    Color Surface,
    Color Card,
    Color Field,
    Color Text,
    Color SecondaryText,
    Color Border,
    Color Hover,
    Color Selected,
    Color Primary,
    Color PrimaryText)
{
    public static FluentPalette Create(bool darkMode) => darkMode
        ? new(
            true,
            Color.FromArgb(32, 32, 32),
            Color.FromArgb(39, 39, 39),
            Color.FromArgb(47, 47, 47),
            Color.FromArgb(36, 36, 36),
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(176, 176, 176),
            Color.FromArgb(69, 69, 69),
            Color.FromArgb(58, 58, 58),
            Color.FromArgb(46, 76, 106),
            Color.FromArgb(96, 160, 220),
            Color.FromArgb(0, 0, 0))
        : new(
            false,
            Color.FromArgb(243, 243, 243),
            Color.FromArgb(249, 249, 249),
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(27, 27, 27),
            Color.FromArgb(96, 96, 96),
            Color.FromArgb(209, 209, 209),
            Color.FromArgb(232, 232, 232),
            Color.FromArgb(218, 235, 250),
            Color.FromArgb(0, 95, 184),
            Color.FromArgb(255, 255, 255));
}

internal static class FluentTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightTheme = "AppsUseLightTheme";
    private const string SystemUsesLightTheme = "SystemUsesLightTheme";

    private sealed class RoleHolder(FluentThemeRole role)
    {
        public FluentThemeRole Role { get; } = role;
    }

    private static readonly ConditionalWeakTable<Control, RoleHolder> Roles = new();

    public static bool IsDarkMode
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return ResolveDarkMode(
                    key?.GetValue(SystemUsesLightTheme),
                    key?.GetValue(AppsUseLightTheme),
                    fallback: false);
            }
            catch (Exception error) when (error is IOException or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    public static FluentPalette Current => FluentPalette.Create(IsDarkMode);

    internal static bool ResolveDarkMode(
        object? systemUsesLightTheme,
        object? appsUseLightTheme,
        bool fallback)
    {
        if (systemUsesLightTheme is int systemValue)
        {
            return systemValue == 0;
        }

        return appsUseLightTheme is int appsValue ? appsValue == 0 : fallback;
    }

    public static void Mark(Control control, FluentThemeRole role)
    {
        Roles.Remove(control);
        Roles.Add(control, new RoleHolder(role));
    }

    public static void Apply(Control root) => ApplyPalette(root, Current);

    // Separado de Apply para poder verificar el tema oscuro sin tocar el del usuario.
    internal static void ApplyPalette(Control root, FluentPalette palette)
    {
        ApplyControl(root, palette);
        root.Invalidate(true);
    }

    private static void ApplyControl(Control control, FluentPalette palette)
    {
        Roles.TryGetValue(control, out RoleHolder? holder);
        FluentThemeRole? role = holder?.Role;
        control.ForeColor = role == FluentThemeRole.SecondaryText ? palette.SecondaryText : palette.Text;

        switch (control)
        {
            case Form:
                control.BackColor = palette.Window;
                break;
            case FluentTableLayoutPanel:
                control.BackColor = palette.Card;
                break;
            case FluentPanel:
                control.BackColor = palette.Border;
                break;
            case ListView listView:
                listView.BackColor = palette.Card;
                listView.BorderStyle = BorderStyle.None;
                // Sin el tema explícito la cabecera de columnas se dibuja clara sobre
                // fondo oscuro; DarkMode_Explorer es el que usa el propio Explorador.
                if (listView.IsHandleCreated)
                {
                    SetWindowTheme(listView.Handle, palette.Dark ? "DarkMode_Explorer" : "Explorer", null);

                    // La cabecera es una ventana aparte y no hereda el tema de la lista.
                    nint header = SendMessage(listView.Handle, ListViewGetHeader, 0, 0);
                    if (header != 0)
                    {
                        SetWindowTheme(header, palette.Dark ? "DarkMode_ItemsView" : "ItemsView", null);
                    }
                }

                break;
            case TextBoxBase:
            case ComboBox:
                control.BackColor = palette.Field;
                break;
            case Button button:
                bool primary = role == FluentThemeRole.PrimaryButton;
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = primary ? palette.Primary : palette.Surface;
                button.ForeColor = primary ? palette.PrimaryText : palette.Text;
                button.FlatAppearance.BorderColor = primary ? palette.Primary : palette.Border;
                button.FlatAppearance.BorderSize = primary ? 0 : 1;
                break;
            case RadioButton radioButton when radioButton.Appearance == Appearance.Button:
                radioButton.UseVisualStyleBackColor = false;
                radioButton.FlatStyle = FlatStyle.Flat;
                radioButton.BackColor = palette.Window;
                radioButton.FlatAppearance.BorderSize = 0;
                radioButton.FlatAppearance.CheckedBackColor = palette.Selected;
                radioButton.FlatAppearance.MouseOverBackColor = palette.Hover;
                break;
            case CheckBox:
            case Label:
                SetTransparent(control, palette.Window);
                break;
            case Panel:
                control.BackColor = role switch
                {
                    FluentThemeRole.Surface => palette.Surface,
                    FluentThemeRole.Border => palette.Border,
                    _ => palette.Window
                };
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyControl(child, palette);
        }
    }

    private static void SetTransparent(Control control, Color fallback)
    {
        control.BackColor = control.Parent?.BackColor ?? fallback;
    }

    private const int ListViewGetHeader = 0x101F;

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(nint window, string? applicationName, string? subIdList);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
}

internal abstract class FluentForm : Form
{
    private const int SystemColorChange = 0x0015;
    private const int SettingChange = 0x001A;
    private const int ThemeChanged = 0x031A;
    private const int DwmColorizationColorChanged = 0x0320;

    private readonly FluentBackdropKind backdrop;
    private bool backdropApplied;
    private bool themeRefreshPending;

    protected FluentForm(FluentBackdropKind backdrop)
    {
        this.backdrop = backdrop;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        Font = FluentTypography.CreateBodyFont();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RefreshTheme();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshTheme();
    }

    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        QueueThemeRefresh();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (!backdropApplied)
        {
            base.OnPaintBackground(e);
        }
    }

    protected override void WndProc(ref Message message)
    {
        int id = message.Msg;
        base.WndProc(ref message);
        if (id is SystemColorChange or SettingChange or ThemeChanged or DwmColorizationColorChanged)
        {
            QueueThemeRefresh();
        }
    }

    private void QueueThemeRefresh()
    {
        if (themeRefreshPending || !IsHandleCreated || IsDisposed)
        {
            return;
        }

        themeRefreshPending = true;
        try
        {
            BeginInvoke(() =>
            {
                themeRefreshPending = false;
                if (!IsDisposed)
                {
                    RefreshTheme();
                }
            });
        }
        catch (InvalidOperationException)
        {
            themeRefreshPending = false;
        }
    }

    protected void RefreshTheme()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        FluentTheme.Apply(this);
        backdropApplied = FluentWindowStyler.Apply(Handle, backdrop, FluentTheme.IsDarkMode);
        Invalidate(true);
    }
}

internal static class FluentTypography
{
    public static Font CreateBodyFont() => CreateFont("Segoe UI Variable Text", 9f, FontStyle.Regular);

    public static Font CreateTitleFont(float size) => CreateFont("Segoe UI Variable Display", size, FontStyle.Bold);

    private static Font CreateFont(string familyName, float size, FontStyle style)
    {
        Font font = new(familyName, size, style, GraphicsUnit.Point);
        if (font.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase))
        {
            return font;
        }

        font.Dispose();
        FontFamily fallbackFamily = SystemFonts.MessageBoxFont?.FontFamily ?? SystemFonts.DefaultFont.FontFamily;
        return new Font(fallbackFamily, size, style, GraphicsUnit.Point);
    }
}

internal class FluentPanel : Panel
{
    [DefaultValue(8)]
    public int CornerRadius { get; set; } = 8;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRoundedRegion();
    }

    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        UpdateRoundedRegion();
    }

    protected void UpdateRoundedRegion()
    {
        if (Width <= 1 || Height <= 1 || SystemInformation.HighContrast)
        {
            Region?.Dispose();
            Region = null;
            return;
        }

        int radius = Math.Max(2, (int)Math.Round(CornerRadius * DeviceDpi / 96f));
        using GraphicsPath path = CreateRoundedPath(ClientRectangle, radius);
        Region? previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        bounds.Width--;
        bounds.Height--;
        int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        Rectangle arc = new(bounds.Location, new Size(diameter, diameter));
        GraphicsPath path = new();
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class FluentTableLayoutPanel : TableLayoutPanel
{
    [DefaultValue(8)]
    public int CornerRadius { get; set; } = 8;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRoundedRegion();
    }

    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        UpdateRoundedRegion();
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 1 || Height <= 1 || SystemInformation.HighContrast)
        {
            Region?.Dispose();
            Region = null;
            return;
        }

        using GraphicsPath path = new();
        int radius = Math.Max(2, (int)Math.Round(CornerRadius * DeviceDpi / 96f));
        int diameter = Math.Min(radius * 2, Math.Min(Width - 1, Height - 1));
        Rectangle arc = new(0, 0, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = Width - diameter - 1;
        path.AddArc(arc, 270, 90);
        arc.Y = Height - diameter - 1;
        path.AddArc(arc, 0, 90);
        arc.X = 0;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        Region? previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }
}
