using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace PCL.UI;

/// <summary>
///     把「长按」映射成「右键」：桌面端用右键触发的操作，在触屏上通过长按触发。
///     Android 外壳在长按命中控件后调用 <see cref="Raise" />，控件用 <see cref="Subscribe" /> 订阅。
/// </summary>
public static class TouchRightClick
{
    private static event Action<Control>? Invoked;

    /// <summary>触发一次长按右键，<paramref name="source" /> 为命中的控件。</summary>
    public static void Raise(Control source)
    {
        Invoked?.Invoke(source);
    }

    /// <summary>订阅：命中 <paramref name="target" /> 或其子控件时执行 <paramref name="handler" />。</summary>
    public static void Subscribe(Control target, Action handler)
    {
        Invoked += source =>
        {
            if (!ReferenceEquals(source, target) && !target.IsVisualAncestorOf(source)) return;
            handler();
        };
    }
}
