using System.Windows.Automation;
using AccessibleOCRStudio;
using AccessibleOCRStudio.Services;

internal static class AuthorInformationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var language in new[] { "en", "ar" })
        {
            UiLanguage.Configure(new AppSettings { InterfaceLanguage = language });
            var window = new AuthorInformationWindow();
            var text = window.AuthorDetails.Text;
            check(text.Contains("iman.rammal@gmail.com"), language + " author panel contains the support email");
            foreach (var link in new[] { "https://www.youtube.com/@ihrammal", "https://t.me/ihrammal", "https://www.facebook.com/ihrammal", "https://x.com/ihrammal", "https://tiktok.com/ihrammal" })
                check(text.Contains(link), language + " author panel contains updated social link " + link);
            check(text.Contains("Microsoft Word") && text.Contains("NVDA"), language + " author panel includes the updated qualification");
            check(!text.Contains("+974") && !text.Contains("tel:") && !text.Contains("Mob:"), language + " author panel excludes personal telephone information");
            check(window.AuthorDetails.IsReadOnly && !window.AuthorDetails.AcceptsTab && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(window.AuthorDetails)), language + " author information is labelled, selectable and does not trap Tab");
            check(window.CloseAction.IsCancel, language + " author panel supports Escape to close");
            window.Close();
        }
        UiLanguage.Configure(new AppSettings { InterfaceLanguage = "en" });
    }
}
