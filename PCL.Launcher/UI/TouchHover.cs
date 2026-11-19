using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace PCL.UI;

/// <summary>
///     触摸下的「悬停」状态：触摸不会设置 <see cref="InputElement.IsPointerOver" />，
///     按下时给命中的控件链打上临时悬停标记，抬起/取消时清掉，让配色反馈在手机上也能出现。
/// </summary>
public static class TouchHover
{
    private static readonly ConditionalWeakTable<Control, StrongBox<bool>> States = new();
    private static readonly List<WeakReference<Control>> PressedControls = [];

    /// <summary>控件当前是否处于悬停态（含触摸按下）。</summary>
    public static bool IsHoveredState(this Control control)
    {
        if (control.IsPointerOver) return true;
        return States.TryGetValue(control, out var state) && state.Value;
    }

    /// <summary>在页面根控件上安装触摸悬停处理。</summary>
    public static void Install(Control root)
    {
        root.AddHandler(InputElement.PointerPressedEvent, OnPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(InputElement.PointerReleasedEvent, OnReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(InputElement.PointerCaptureLostEvent, OnReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type is not PointerType.Touch) return;
        ClearPressed();
        SetChain(e.Source as Visual, true);
    }

    private static void OnReleased(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Type is not PointerType.Touch) return;
        ClearPressed();
    }

    /// <summary>记录按下时被点亮的所有控件：滑动后 PointerReleased 的 Source 会变，
    /// 只按 Source 还原会把沿途控件永久留在悬停态。</summary>
    private static void ClearPressed()
    {
        foreach (var reference in PressedControls)
        {
            if (!reference.TryGetTarget(out var control)) continue;
            var state = States.GetOrCreateValue(control);
            if (!state.Value) continue;
            state.Value = false;
            control.InvalidateVisual();
        }

        PressedControls.Clear();
    }

    private static void SetChain(Visual? source, bool value)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is not Control control) continue;
            var state = States.GetOrCreateValue(control);
            if (state.Value == value) continue;
            state.Value = value;
            if (value) PressedControls.Add(new WeakReference<Control>(control));
            control.InvalidateVisual();
        }
    }
}
