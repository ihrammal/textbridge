using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AccessibleOCRStudio;
using AccessibleOCRStudio.Services;

internal static class AccessibilityChecks
{
    internal static void Run(string temp, Action<bool, string> check)
    {
        UiLanguage.Configure(new AppSettings { InterfaceLanguage = "en" });
        var window = new MainWindow();
        var settings = (AppSettings)typeof(MainWindow).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var editor = (TextBox)window.FindName("DocumentText");
        var status = (System.Windows.Controls.Primitives.StatusBar)window.FindName("ApplicationStatusBar");
        var statusMenu = (MenuItem)window.FindName("ShowStatusBarMenu");
        void Apply() => typeof(MainWindow).GetMethod("SyncAccessibilityMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
        settings.ShowStatusBar = false;
        Apply();
        check(status.Visibility == Visibility.Collapsed && !statusMenu.IsChecked, "Hidden status bar is removed from layout and matches the View toggle");
        settings.ShowStatusBar = true;
        Apply();
        check(status.Visibility == Visibility.Visible && statusMenu.IsChecked, "Status bar can be shown again without restarting");
        settings.ShowStatusBar = false;
        settings.DocumentFontFamily = "Tahoma";
        settings.DocumentFontSize = 32;
        settings.InterfaceFontSize = 20;
        settings.DocumentBold = true;
        settings.ReadingColors = "yellow";
        Apply();
        check(editor.FontFamily.Source == "Tahoma" && Math.Abs(editor.FontSize - 32.0 * 96 / 72) < 0.01 && editor.FontWeight == FontWeights.Bold && window.FontSize == 20,
            "Low-vision font family, point size, bold and interface size apply to the actual controls");
        if (!SystemParameters.HighContrast)
            check(((SolidColorBrush)editor.Background).Color == Colors.Black && ((SolidColorBrush)editor.Foreground).Color == Colors.Yellow &&
                ((SolidColorBrush)editor.CaretBrush).Color == Colors.Yellow, "High-contrast reading colours include the text and insertion caret");
        settings.ReadingColors = "system";
        Apply();
        check(editor.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue &&
            editor.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue,
            "Returning to Windows colours clears custom foreground and background overrides");
        check(ReadingAppearance.Size(double.NaN, 18, 10, 72) == 18 && ReadingAppearance.Size(500, 18, 10, 72) == 18,
            "Invalid saved font sizes fall back to readable defaults");
        var path = Path.Combine(temp, "reading-settings.json");
        settings.Save(path);
        var restored = AppSettings.Load(path);
        check(!restored.ShowStatusBar && restored.DocumentFontFamily == "Tahoma" && restored.DocumentFontSize == 32 && restored.DocumentBold,
            "Status visibility and low-vision preferences survive saving and reloading");
        var preferences = new SettingsWindow(settings);
        preferences.OpenAccessibility();
        check(((TabControl)preferences.FindName("Tabs")).Items.Count == 4 &&
            ((TabControl)preferences.FindName("Tabs")).SelectedItem == preferences.FindName("AccessibilityTab") &&
            preferences.FindName("ScreenReaderGroup") is GroupBox && preferences.FindName("LowVisionGroup") is GroupBox &&
            preferences.FindName("SoundsGroup") is GroupBox,
            "A single Accessibility tab contains screen-reader, low-vision and sound groups");
        var engine = (ComboBox)preferences.FindName("OcrEngine");
        engine.SelectedItem = engine.Items.OfType<ComboBoxItem>().First(item => (string?)item.Tag == "windows");
        check(System.Windows.Automation.AutomationProperties.GetHelpText(engine).Contains("confidence is unavailable") &&
            System.Windows.Automation.AutomationProperties.GetLiveSetting((TextBlock)preferences.FindName("EngineHint")) == System.Windows.Automation.AutomationLiveSetting.Polite,
            "Changing OCR engine exposes its limitations as help text and a polite announcement");
        preferences.OpenLowVision();
        check(((TabControl)preferences.FindName("Tabs")).SelectedItem == preferences.FindName("AccessibilityTab") &&
            ((ComboBox)preferences.FindName("ReadingColors")).Items.Count == 5 &&
            ((ComboBox)preferences.FindName("DocumentSize")).Text == "32", "Reading appearance opens the shared Accessibility tab with saved low-vision values");
        check(((CheckBox)preferences.FindName("CheckUpdates")).Content.ToString()!.Contains("TextBridge") &&
            !((CheckBox)preferences.FindName("CheckUpdates")).Content.ToString()!.Contains("Clear Read"),
            "Update settings display TextBridge rather than the former product name");
        check(ApplicationCommands.Cut.CanExecute(null, editor) && ApplicationCommands.Copy.CanExecute(null, editor),
            "Cut and Copy remain reachable in Edit even before text is selected");
        editor.Text = "Keep this text";
        editor.Select(0, 0);
        ApplicationCommands.Cut.Execute(null, editor);
        check(editor.Text == "Keep this text" && ((TextBlock)window.FindName("StatusText")).Text.Contains("Select text"),
            "Cut without a selection gives guidance and does not remove document text");
        var file = (MenuItem)window.FindName("FileMenu");
        var export = file.Items.OfType<MenuItem>().First(item => item.HasItems);
        settings.CheckUpdatesOnStartup = false;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Opacity = 0;
        window.Show();
        window.UpdateLayout();
        file.IsSubmenuOpen = true;
        export.IsSubmenuOpen = true;
        check(file.IsSubmenuOpen && export.IsSubmenuOpen, "Nested menu navigation is initialized on a loaded window");
        void Key(Key key, bool release = false)
        {
            var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key)
            { RoutedEvent = release ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent };
            window.RaiseEvent(input);
        }
        Key(System.Windows.Input.Key.Escape);
        check(!export.IsSubmenuOpen && file.IsSubmenuOpen, "Escape closes the nested export menu while keeping the File menu open");
        Key(System.Windows.Input.Key.Escape);
        check(!file.IsSubmenuOpen, "A further Escape closes the File dropdown one level at a time");
        file.IsSubmenuOpen = true;
        export.IsSubmenuOpen = true;
        Key(System.Windows.Input.Key.LeftAlt);
        Key(System.Windows.Input.Key.LeftAlt, true);
        check(!file.IsSubmenuOpen && !export.IsSubmenuOpen, "Alt closes the complete menu chain rather than stepping back like Escape");
        UiLanguage.Configure(new AppSettings { InterfaceLanguage = "ar" });
        UiLanguage.Apply(preferences);
        check(((TabItem)preferences.FindName("AccessibilityTab")).Header.ToString() == "إمكانية الوصول" &&
            UiLanguage.T("Show status bar") == "إظهار شريط الحالة", "Low-vision and status visibility controls have Arabic labels");
        preferences.Close();
        editor.Clear();
        window.Close();
        UiLanguage.Configure(new AppSettings { InterfaceLanguage = "en" });
    }
}
