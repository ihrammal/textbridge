using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AccessibleOCRStudio.Services;

namespace AccessibleOCRStudio;

public sealed class AuthorInformationWindow : Window
{
    public TextBox AuthorDetails { get; }
    public Button CloseAction { get; }

    public static string BuildDetails() => string.Join(Environment.NewLine, new[]
    {
        "TextBridge " + typeof(AuthorInformationWindow).Assembly.GetName().Version!.ToString(3),
        UiLanguage.T("By Iman Rammal"),
        "",
        UiLanguage.T("Braille Training Specialist"),
        UiLanguage.T("NVDA Certified Expert"),
        UiLanguage.T("Microsoft Word with NVDA Certified Expert"),
        UiLanguage.T("JAWS Certified"),
        UiLanguage.T("Assistive technology trainer"),
        UiLanguage.T("Translator / interpreter"),
        "",
        UiLanguage.T("Email") + ": " + ProblemReportService.SupportEmail,
        UiLanguage.T("YouTube") + ": https://www.youtube.com/@ihrammal",
        UiLanguage.T("Telegram") + ": https://t.me/ihrammal",
        UiLanguage.T("Facebook") + ": https://www.facebook.com/ihrammal",
        "X: https://x.com/ihrammal",
        UiLanguage.T("TikTok") + ": https://tiktok.com/ihrammal"
    });

    public AuthorInformationWindow()
    {
        Title = UiLanguage.T("About TextBridge");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Width = Math.Min(720, SystemParameters.WorkArea.Width);
        Height = Math.Min(680, SystemParameters.WorkArea.Height);
        MaxWidth = SystemParameters.WorkArea.Width;
        MaxHeight = SystemParameters.WorkArea.Height;
        MinWidth = 300;
        MinHeight = 250;
        FlowDirection = UiLanguage.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var layout = new Grid { Margin = new Thickness(16) };
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AuthorDetails = new TextBox
        {
            Text = BuildDetails(), IsReadOnly = true, AcceptsReturn = true,
            AcceptsTab = false, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 16
        };
        AutomationProperties.SetName(AuthorDetails, UiLanguage.T("Author and contact information"));
        layout.Children.Add(AuthorDetails);
        CloseAction = new Button
        {
            Content = UiLanguage.T("Close"), IsCancel = true, MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0)
        };
        CloseAction.Click += (_, _) => Close();
        Grid.SetRow(CloseAction, 1);
        layout.Children.Add(CloseAction);
        Content = layout;
        Loaded += (_, _) => AuthorDetails.Focus();
    }
}
