using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AccessibleOCRStudio;
using AccessibleOCRStudio.Services;

internal static class MenuChecks
{
    private static char? AccessKey(string text)
    {
        for (var i = 0; i < text.Length - 1; i++)
            if (text[i] == '_')
            {
                if (text[i + 1] == '_') { i++; continue; }
                return char.ToUpperInvariant(text[i + 1]);
            }
        return null;
    }

    private static bool Unique(IEnumerable<string> labels)
    {
        var keys = labels.Select(AccessKey).Where(value => value.HasValue).ToArray();
        return keys.Length == keys.Distinct().Count();
    }

    private static bool MenusUnique(ItemsControl parent)
    {
        var items = parent.Items.OfType<MenuItem>().ToArray();
        return Unique(items.Select(item => item.Header?.ToString() ?? "")) && items.All(MenusUnique);
    }

    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Tree(child)) yield return descendant;
    }

    internal static void Run(string temp, Action<bool, string> check)
    {
        UiLanguage.Configure(new AppSettings { InterfaceLanguage = "en" });
        var main = new MainWindow();
        foreach (var language in new[] { "en", "ar", "en" })
        {
            UiLanguage.Configure(new AppSettings { InterfaceLanguage = language });
            UiLanguage.Apply(main);
            check(MenusUnique((Menu)main.FindName("ApplicationMenu")),
                "Menu access letters remain unique after switching interface language to " + language);
        }
        typeof(MainWindow).GetField("_allowClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, true);
        main.Close();
        foreach (var language in new[] { "en", "ar" })
        {
            UiLanguage.Configure(new AppSettings { InterfaceLanguage = language });
            var privacy = new ApiSettingsWindow(new AppSettings { ApiProvider = "OpenAI" }, new CredentialService(Path.Combine(temp, "menu-keys")));
            var report = new ReportProblemWindow(new AppSettings());
            foreach (var dialog in new Window[] { privacy, report })
            {
                UiLanguage.Apply(dialog);
                var labels = Tree(dialog).Select(node => node switch
                {
                    Button { Content: string content } => content,
                    Label { Content: string content } => content,
                    _ => ""
                });
                check(Unique(labels), dialog.GetType().Name + " has nonconflicting field/button access letters in " + language);
                dialog.Close();
            }
        }
        UiLanguage.Configure(new AppSettings { InterfaceLanguage = "en" });
    }
}
