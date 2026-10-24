using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SL.Pres.Infrastructure;

/// <summary>全部 ViewModel 的基类:最小 INPC 实现。</summary>
public abstract class VmBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void On([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        On(name);
        return true;
    }
}
