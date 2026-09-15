using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace RightBTRadio.WinUI;

/// <summary>
/// Hace que los botones alojados en la barra de título propia reciban clics.
/// </summary>
/// <remarks>
/// Con <c>ExtendsContentIntoTitleBar</c> toda la franja de título es región de arrastre y
/// se traga el puntero: un botón puesto ahí se ve pero no responde. El mecanismo nativo
/// para exceptuar zonas es registrarlas como <see cref="NonClientRegionKind.Passthrough"/>.
/// Tres trampas, verificables solo en vivo:
/// 1. Los rectángulos van en píxeles físicos: hay que multiplicar por
///    <see cref="XamlRoot.RasterizationScale"/>.
/// 2. El recálculo va en el <c>SizeChanged</c> de la barra y no en el <c>Changed</c> de la
///    ventana, que dispara antes de que la barra se redimensione.
/// 3. Las regiones se pierden al cambiar la escala en caliente (microsoft-ui-xaml#10151),
///    así que también se recalculan ante cambios de escala.
/// </remarks>
internal static class TitleBarInteraction
{
    /// <summary>
    /// Hueco lógico entre el último botón y los de la ventana, además del <c>RightInset</c>.
    /// Pegados, se leen como una sola hilera; las aplicaciones de Windows separan con espacio.
    /// </summary>
    private const double SystemButtonsGap = 12;

    public static void Track(
        Window window,
        FrameworkElement titleBar,
        FrameworkElement cluster,
        params FrameworkElement[] interactiveElements)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported() || window.AppWindow is null)
        {
            return;
        }

        InputNonClientPointerSource source = InputNonClientPointerSource.GetForWindowId(window.AppWindow.Id);
        AppWindowTitleBar appTitleBar = window.AppWindow.TitleBar;
        double lastScale = 0;

        void Update()
        {
            XamlRoot? root = titleBar.XamlRoot;
            if (root is null || titleBar.ActualWidth <= 0)
            {
                return;
            }

            lastScale = root.RasterizationScale;

            // RightInset viene en píxeles físicos y cambia con el DPI. Sin él, el último botón
            // queda tapado por minimizar, maximizar y cerrar.
            cluster.Margin = new Thickness(0, 0, (appTitleBar.RightInset / lastScale) + SystemButtonsGap, 0);
            cluster.UpdateLayout();

            List<RectInt32> rects = new(interactiveElements.Length);
            foreach (FrameworkElement element in interactiveElements)
            {
                if (element.ActualWidth <= 0 || element.ActualHeight <= 0 || element.Visibility != Visibility.Visible)
                {
                    continue;
                }

                Windows.Foundation.Point origin = element.TransformToVisual(titleBar)
                    .TransformPoint(new Windows.Foundation.Point(0, 0));
                rects.Add(new RectInt32(
                    (int)Math.Round(origin.X * lastScale),
                    (int)Math.Round(origin.Y * lastScale),
                    (int)Math.Round(element.ActualWidth * lastScale),
                    (int)Math.Round(element.ActualHeight * lastScale)));
            }

            // SetRegionRects reemplaza el conjunto; vacío hay que limpiar a mano o quedan vivas
            // las regiones del layout anterior.
            if (rects.Count > 0)
            {
                source.SetRegionRects(NonClientRegionKind.Passthrough, [.. rects]);
            }
            else
            {
                source.ClearRegionRects(NonClientRegionKind.Passthrough);
            }
        }

        // Antes de Loaded las medidas son cero y las regiones quedarían vacías.
        if (titleBar.IsLoaded)
        {
            Update();
        }
        else
        {
            titleBar.Loaded += (_, _) => Update();
        }

        titleBar.SizeChanged += (_, _) => Update();
        titleBar.Loaded += (_, _) =>
        {
            if (titleBar.XamlRoot is { } xamlRoot)
            {
                xamlRoot.Changed += (sender, _) =>
                {
                    if (Math.Abs(sender.RasterizationScale - lastScale) > 0.001)
                    {
                        Update();
                    }
                };
            }
        };
    }
}
