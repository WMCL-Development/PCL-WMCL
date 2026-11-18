using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PCL.Android;

/// <summary>
///     触摸屏没有右键，这里把「长按」映射成右键菜单：
///     按住 500ms 且没有明显移动时，打开命中的控件（或其祖先）上的 <see cref="ContextMenu" />；
///     没有菜单时再抛出 <see cref="Control.ContextRequestedEvent" />，交给页面自己的处理器。
/// </summary>
public static class LongPressContextMenu
{
    private const int HoldMilliseconds = 500;
    private const double MoveTolerance = 12d;

    public static void Install(Control root)
    {
        var state = new State(root);
        root.AddHandler(InputElement.PointerPressedEvent, state.OnPressed, RoutingStrategies.Tunnel, true);
        root.AddHandler(InputElement.PointerMovedEvent, state.OnMoved, RoutingStrategies.Tunnel, true);
        root.AddHandler(InputElement.PointerReleasedEvent, state.OnReleased, RoutingStrategies.Tunnel, true);
        root.AddHandler(InputElement.PointerCaptureLostEvent, state.OnCaptureLost, RoutingStrategies.Tunnel, true);
    }

    private sealed class State(Control root)
    {
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(HoldMilliseconds) };
        private Point _origin;
        private Visual? _source;
        private bool _armed;

        public void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            var properties = e.GetCurrentPoint(root).Properties;
            if (properties.IsRightButtonPressed)
                return;

            _source = e.Source as Visual;
            _origin = e.GetPosition(root);
            _armed = _source is not null;

            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public void OnMoved(object? sender, PointerEventArgs e)
        {
            if (!_armed) return;

            var delta = e.GetPosition(root) - _origin;
            if (Math.Abs(delta.X) > MoveTolerance || Math.Abs(delta.Y) > MoveTolerance)
                Cancel();
        }

        public void OnReleased(object? sender, PointerReleasedEventArgs e) => Cancel();

        public void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e) => Cancel();

        private void Cancel()
        {
            _armed = false;
            _source = null;
            _timer.Stop();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            _timer.Stop();
            if (!_armed || _source is not { } source) return;
            _armed = false;

            try
            {
                for (var current = source; current is not null; current = current.GetVisualParent())
                {
                    if (current is not Control control) continue;
                    if (control.ContextMenu is { } menu)
                    {
                        menu.Open(control);
                        return;
                    }
                }

                // 先按「右键」语义通知订阅者，再抛 ContextRequested 交给页面处理。
                if (source is Control target)
                {
                    PCL.UI.TouchRightClick.Raise(target);
                    target.RaiseEvent(new ContextRequestedEventArgs());
                }
            }
            catch (Exception exception)
            {
                AndroidBootstrap.RecordFatal("longpress:ContextMenu", exception);
            }
            finally
            {
                _source = null;
            }
        }
    }
}
