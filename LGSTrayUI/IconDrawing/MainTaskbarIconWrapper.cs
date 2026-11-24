using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace LGSTrayUI.IconDrawing;

/// <summary>One persistent application icon, independent of all device ring icons.</summary>
public class MainTaskbarIconWrapper : IDisposable
{
    private TaskbarIcon? icon;
    private Icon? artwork;
    public void SetContextMenuDataContext(object dataContext) { }
    internal void Initialize(NotifyIconViewModel vm,Action settings,Action? rescan=null,Action? manage=null,Func<int>? onlineCount=null){
        if(icon!=null)return;
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("BatteryMonitor.ico")??throw new InvalidOperationException("Missing application tray icon");
        using var source=new Icon(stream);artwork=(Icon)source.Clone();
        var menu=BatteryMenu.Create(vm,settings,rescan,manage,onlineCount);
        icon=new TaskbarIcon{Icon=artwork,ToolTipText="Battery Monitor",ContextMenu=menu};
    }
    public void Dispose(){icon?.Dispose();icon=null;artwork?.Dispose();artwork=null;}
}
