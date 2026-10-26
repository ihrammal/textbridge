using System.ComponentModel;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace AccessibleOCRStudio;

internal static class LiveRegionAnnouncements
{
    internal static readonly System.Windows.DependencyProperty SuppressAutomaticProperty =
        System.Windows.DependencyProperty.RegisterAttached("SuppressAutomatic", typeof(bool), typeof(LiveRegionAnnouncements), new System.Windows.PropertyMetadata(false));

    internal static void SetSilently(TextBlock text, string message)
    {
        text.SetValue(SuppressAutomaticProperty, true);
        try { text.Text = message; }
        finally { text.SetValue(SuppressAutomaticProperty, false); }
    }

    // Explicit notifications may repeat the same status on a long-running page.
    internal static void Announce(TextBlock text, System.Windows.UIElement? fallback, string message)
    {
        if (!text.IsLoaded || AutomationProperties.GetLiveSetting(text) == AutomationLiveSetting.Off) return;
        var target = text.IsVisible || fallback is null ? (System.Windows.UIElement)text : fallback;
        var peer = UIElementAutomationPeer.FromElement(target) ?? UIElementAutomationPeer.CreatePeerForElement(target);
        if (peer is null) return;
        try
        {
            peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, message, "TextBridge status");
        }
        catch
        {
            if (ToastAnnouncements.IsMilestone(message)) ToastAnnouncements.AnnounceMilestone(message, text);
            else peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    public static void Attach(TextBlock text, System.Windows.UIElement? fallback = null)
    {
        var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        EventHandler changed = (_, _) =>
            {
                if (!text.IsLoaded || (bool)text.GetValue(SuppressAutomaticProperty)) return;
                var message = text.Text ?? string.Empty;
                if (AutomationProperties.GetLiveSetting(text) == AutomationLiveSetting.Off) return;
                Announce(text, fallback, message);
            };
        text.Loaded += (_, _) => descriptor.AddValueChanged(text, changed);
        text.Unloaded += (_, _) => descriptor.RemoveValueChanged(text, changed);
    }
}
