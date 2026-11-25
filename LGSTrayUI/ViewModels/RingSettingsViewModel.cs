using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;

namespace LGSTrayUI;

// View models own edits, validation and persistence notifications; the
// existing programmatic WPF view remains responsible only for presentation.
internal sealed class SettingField<T>:INotifyPropertyChanged
{
    private T value;
    private readonly Action<T> apply;
    private readonly Func<T,T> normalize;
    internal SettingField(T initial,Action<T> apply,Func<T,T>? normalize=null){
        value=initial;this.apply=apply;this.normalize=normalize??(v=>v);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public T Value {
        get=>value;
        set {
            var next=normalize(value);
            if(System.Collections.Generic.EqualityComparer<T>.Default.Equals(this.value,next))return;
            apply(next);this.value=next;
            PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Value)));
        }
    }
}
internal sealed class RingSettingsViewModel
{
    private readonly Action notify;
    internal RingSettingsViewModel(Action? notify=null){this.notify=notify??RingConfig.Notify;}
    internal void Apply<T>(Action<T> set,T value){set(value);notify();}
    internal SettingField<T> Field<T>(T initial,Action<T> set,Func<T,T>? normalize=null)
        =>new(initial,v=>Apply(set,v),normalize);
    internal static double NormalizeNumber(double n,double min,double max,double step)
        =>double.IsFinite(n)?Math.Clamp(min+Math.Round((n-min)/step)*step,min,max):min;
    internal static bool TryNumber(string text,double min,double max,double step,out double value){
        value=0;
        if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n))return false;
        value=NormalizeNumber(n,min,max,step);return true;
    }
}
