using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LGSTrayUI;

[Flags]internal enum PreferenceArea { None=0,Identity=1,Layout=2,Providers=4,Presentation=8,Notifications=16,All=31 }
internal sealed record PreferenceFingerprint(string Identity,string Layout,string Providers,string Presentation,string Notifications)
{
    private static string J(object value)=>JsonSerializer.Serialize(value);
    internal static PreferenceFingerprint Capture(RingPreferences c)=>new(
        J(new{c.AutoAdd,c.AutoDeduplicate,c.DeviceAliases,c.AutomaticAliases,c.SeparateDeviceSources,c.IgnoredDevices,c.PreferredSources,c.Names,c.PhysicalSourceIds,c.PhysicalEvidenceIds,c.PhysicalSourceEvidence}),
        J(new{c.Size,c.Gap,c.LeftGap,c.RightGap,c.X,c.Y,c.Divider,c.DividerWidth,c.DividerHeight,c.Order,c.Hidden,c.AutoHide,c.Hover,c.Animate,c.Motion,c.Speed,c.GrayMs,c.WaitMs,c.OutMs,c.InMs,c.SpringMs,c.StaggerMs,c.Bounce}),
        J(new{c.UseBle,c.UseGip,c.UseXInput,c.AsusHidEnabled,c.UseAstro,c.UseControllerHid,c.GameSirPassiveEnabled,c.PollSeconds,c.BleSeconds}),
        J(RingThemes.Snapshot(c)),
        J(new{c.Alerts,c.Sound,c.ConnectionAlerts,c.FullChargeAlerts,c.QuietNotifications,c.SuppressFullScreen,c.NotificationSeconds,c.NotificationWidth,c.NotificationOpacity,c.NotificationCorner,c.NotificationMonitor,c.Rules,c.DeviceRules,c.AlertSounds}));
    internal PreferenceArea Diff(PreferenceFingerprint other){
        PreferenceArea a=PreferenceArea.None;
        if(Identity!=other.Identity)a|=PreferenceArea.Identity;
        if(Layout!=other.Layout)a|=PreferenceArea.Layout;
        if(Providers!=other.Providers)a|=PreferenceArea.Providers;
        if(Presentation!=other.Presentation)a|=PreferenceArea.Presentation;
        if(Notifications!=other.Notifications)a|=PreferenceArea.Notifications;
        return a;
    }
}
