using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PCL.UI;

/// <summary>
///     触摸手势仲裁：手势起始于可拖拽控件（3D 模型旋转、调色盘取色、滑条等）时，
///     把外层 ScrollViewer 的滚动偏移锁回原处，避免控件拖动被页面滚动抢走。
///     Avalonia 的 ScrollGestureRecognizer 没有公开开关，只能以“拖动期间锁偏移”的方式实现判定。
/// </summary>
public static class ScrollGestureLock
{
    private const int InertiaLockMilliseconds = 900;

    public static void Attach(Control source)
    {
        ArgumentNullException.ThrowIfNull(source);

        ScrollViewer? viewer = null;
        Vector? pinnedOffset = null;
        IPointer? activePointer = null;
        DispatcherTimer? inertiaTimer = null;

        void StopInertiaLock()
        {
            inertiaTimer?.Stop();
            inertiaTimer = null;
        }

        void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Pointer.Type != PointerType.Touch) return;

            viewer ??= source.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
            if (viewer is null) return;

            StopInertiaLock();
            pinnedOffset = viewer.Offset;
            activePointer = e.Pointer;
        }

        void OnMoved(object? sender, PointerEventArgs e)
        {
            if (pinnedOffset is not { } pinned || viewer is null) return;
            if (activePointer is not null && e.Pointer != activePointer) return;
            // 同一个输入帧内压回去，渲染前生效，画面上不会看到闪动。
            if (viewer.Offset != pinned) viewer.Offset = pinned;
        }

        void OnReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (pinnedOffset is not { } pinned || viewer is null) return;
            if (activePointer is not null && e.Pointer != activePointer) return;

            pinnedOffset = null;
            activePointer = null;

            // 抬手后 ScrollGestureRecognizer 还会跑一段惯性滚动，短时间内持续压回去。
            viewer.Offset = pinned;
            var deadline = DateTime.UtcNow.AddMilliseconds(InertiaLockMilliseconds);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            timer.Tick += (_, _) =>
            {
                if (viewer is not null && viewer.Offset != pinned)
                    viewer.Offset = pinned;
                if (DateTime.UtcNow >= deadline)
                {
                    timer.Stop();
                    if (ReferenceEquals(inertiaTimer, timer)) inertiaTimer = null;
                }
            };
            inertiaTimer = timer;
            timer.Start();
        }

        void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            StopInertiaLock();
            pinnedOffset = null;
            activePointer = null;
        }

        source.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, true);
        source.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, true);
        source.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, true);
        source.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Bubble, true);
    }
}
