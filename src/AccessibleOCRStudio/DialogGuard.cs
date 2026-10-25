using System.Windows;

namespace AccessibleOCRStudio;

internal static class DialogGuard
{
    private static bool _active;

    public static bool TryEnter()
    {
        if (_active) return false;
        _active = true;
        return true;
    }

    public static void Exit() => _active = false;

    public static MessageBoxResult Show(Window owner, string text, string caption = "TextBridge",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.OK)
    {
        // A validation prompt owned by the current modal dialog is necessary
        // feedback; a competing prompt owned by the disabled main window is not.
        var ownedPrompt = _active && owner.Owner is not null && owner.IsActive;
        if (!ownedPrompt && !TryEnter()) return Absorb(buttons);
        try
        {
            if (Services.UiLanguage.IsArabic) return ShowArabic(owner, text, caption, buttons, defaultResult);
            return MessageBox.Show(owner, text, caption, buttons, image, defaultResult);
        }
        finally { if (!ownedPrompt) Exit(); }
    }

    public static bool? ShowDialog(Window dialog)
    {
        if (!TryEnter()) return null;
        try { Services.UiLanguage.Apply(dialog); return dialog.ShowDialog(); }
        finally { Exit(); }
    }

    public static bool? Run(Func<bool?> opener)
    {
        if (!TryEnter()) return false;
        try { return opener(); }
        finally { Exit(); }
    }

    internal static MessageBoxResult Absorb(MessageBoxButton buttons) =>
        buttons == MessageBoxButton.OK ? MessageBoxResult.OK
        : buttons == MessageBoxButton.YesNo ? MessageBoxResult.No
        : MessageBoxResult.Cancel;

    private static MessageBoxResult ShowArabic(Window owner, string text, string caption,
        MessageBoxButton buttons, MessageBoxResult defaultResult)
    {
        var result = Absorb(buttons);
        var dialog = new Window { Owner = owner, Title = Services.UiLanguage.T(caption), Width = 560,
            SizeToContent = SizeToContent.Height, MaxHeight = SystemParameters.WorkArea.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            FlowDirection = FlowDirection.RightToLeft, ResizeMode = ResizeMode.NoResize };
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        var message = new System.Windows.Controls.TextBlock { Text = Services.UiLanguage.T(text),
            TextWrapping = TextWrapping.Wrap, Focusable = true, FontSize = 16 };
        panel.Children.Add(new System.Windows.Controls.ScrollViewer { Content = message,
            MaxHeight = Math.Max(100, SystemParameters.WorkArea.Height - 180),
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto });
        var actions = new System.Windows.Controls.WrapPanel { Margin = new Thickness(0,16,0,0), HorizontalAlignment = HorizontalAlignment.Left };
        var choices = buttons switch
        {
            MessageBoxButton.YesNo => new[] { MessageBoxResult.Yes, MessageBoxResult.No },
            MessageBoxButton.YesNoCancel => new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel },
            MessageBoxButton.OKCancel => new[] { MessageBoxResult.OK, MessageBoxResult.Cancel },
            _ => new[] { MessageBoxResult.OK }
        };
        if (!choices.Contains(defaultResult)) defaultResult = Absorb(buttons);
        foreach (var choice in choices)
        {
            var button = new System.Windows.Controls.Button { Content = choice switch
                { MessageBoxResult.Yes => "نعم", MessageBoxResult.No => "لا", MessageBoxResult.Cancel => "إلغاء", _ => "موافق" },
                MinWidth = 85, Padding = new Thickness(8,4,8,4), Margin = new Thickness(4), IsDefault = choice == defaultResult,
                IsCancel = choice == Absorb(buttons) };
            button.Click += (_, _) => { result = choice; dialog.Close(); };
            actions.Children.Add(button);
        }
        panel.Children.Add(actions); dialog.Content = panel; dialog.ShowDialog(); return result;
    }
}

