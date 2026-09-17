namespace RightBTRadio;

/// <summary>
/// Textos, medidas e identificadores de automatización que la ventana de ajustes
/// promete. El arnés de interfaz los lee de aquí en lugar de copiarlos, para que un
/// cambio de contrato sin cambio de interfaz —o al revés— rompa el arnés a propósito.
/// </summary>
/// <remarks>
/// Mismo criterio que <c>SettingsPanelVisualContract</c> de RightKeyboard. Los
/// <c>AutomationId</c> se declaran aquí porque los slugs que devuelve <c>winapp inspect</c>
/// llevan un sufijo derivado del RuntimeId y cambian entre ejecuciones.
/// </remarks>
internal static class SettingsVisualContract
{
    public const string WindowTitle = "Ajustes de RightBTRadio";

    public const string Subtitle =
        "Mantiene habilitado un solo radio Bluetooth cuando hay varios conectados.";

    public const string DevicesDescription =
        "Los radios detectados en este equipo y los del grupo que no están conectados. " +
        "El punto verde indica que el radio está conectado.";

    public const string PriorityDescription =
        "Queda habilitado el primer radio conectado de la lista. Los demás se deshabilitan.";

    public const int MinimumWidth = 1120;

    public const int MinimumHeight = 720;

    public const string SubtitleId = "Subtitle";

    public const string DevicesListId = "DevicesList";

    public const string PriorityListId = "PriorityList";

    public const string AliasTextBoxId = "AliasTextBox";

    public const string AddButtonId = "AddToGroupButton";

    public const string RemoveButtonId = "RemoveFromGroupButton";

    public const string MoveUpButtonId = "MoveUpButton";

    public const string MoveDownButtonId = "MoveDownButton";

    public const string SettingsButtonId = "SettingsButton";

    public const string AboutButtonId = "AboutButton";

    public const string HelpButtonId = "HelpButton";

    /// <summary>Dentro del flyout de ajustes, igual que los dos interruptores.</summary>
    public const string ThemeRadioButtonsId = "ThemeRadioButtons";

    public const string StartWithWindowsToggleId = "StartWithWindowsToggle";

    public const string StartMinimizedToggleId = "StartMinimizedToggle";

    public const string DevicesDescriptionId = "DevicesDescription";

    public const string PriorityDescriptionId = "PriorityDescription";
}
