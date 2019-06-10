﻿using Reactive.Bindings.Extensions;
using System;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Input;

namespace ZoomThumb.Views.Common
{
    public static class MouseObservableExtensions
    {

        public static IObservable<MouseEventArgs> MouseLeaveAsObservable(this UIElement control)
            => Observable.FromEvent<MouseEventHandler, MouseEventArgs>
            (
                handler => (sender, e) => handler(e),
                handler => control.MouseLeave += handler,
                handler => control.MouseLeave -= handler
            );

        #region MouseLeftButton

        public static IObservable<MouseEventArgs> MouseLeftButtonDownAsObservable(this UIElement control, bool handled = false)
            => Observable.FromEvent<MouseButtonEventHandler, MouseButtonEventArgs>
            (
                handler => (sender, e) => { e.Handled = handled; handler(e); },
                handler => control.MouseLeftButtonDown += handler,
                handler => control.MouseLeftButtonDown -= handler
            );

        public static IObservable<MouseEventArgs> MouseLeftButtonUpAsObservable(this UIElement control, bool handled = false)
            => Observable.FromEvent<MouseButtonEventHandler, MouseButtonEventArgs>
            (
                handler => (sender, e) => { e.Handled = handled; handler(e); },
                handler => control.MouseLeftButtonUp += handler,
                handler => control.MouseLeftButtonUp -= handler
            );

        #endregion

        #region MouseMove

        public static IObservable<MouseEventArgs> MouseMoveAsObservable(this UIElement control, bool handled = false)
            => Observable.FromEvent<MouseEventHandler, MouseEventArgs>
            (
                handler => (sender, e) => { e.Handled = handled; handler(e); },
                handler => control.MouseMove += handler,
                handler => control.MouseMove -= handler
            );

        // 指定コントロール上のマウスポインタの絶対座標を取得
        public static IObservable<Point> MouseMovePointAsObservable(this UIElement control) =>
            control.MouseMoveAsObservable().Select(e => e.GetPosition((IInputElement)control));

        /// <summary>
        /// マウスクリック中の移動量を流す
        /// </summary>
        /// <param name="control">対象コントロール</param>
        /// <param name="originControl">マウス移動量の原点コントロール</param>
        /// <returns>移動量</returns>
        public static IObservable<Vector> MouseLeftClickMoveAsObservable(this UIElement control, bool handled = false, IInputElement originControl = null)
        {
            if (originControl is null) originControl = control;

            var mouseDown = control.MouseLeftButtonDownAsObservable(handled).ToUnit();
            var mouseUp = control.MouseLeftButtonUpAsObservable(handled).ToUnit();

            return control.MouseMoveAsObservable(handled)
                .Select(e => e.GetPosition(originControl))
                .Pairwise().Select(x => x.NewItem - x.OldItem)
                .SkipUntil(mouseDown)
                .TakeUntil(mouseUp)
                .Repeat();
        }

        #endregion

        public static IObservable<MouseWheelEventArgs> PreviewMouseWheelAsObservable(this UIElement control, bool handled = false)
            => Observable.FromEvent<MouseWheelEventHandler, MouseWheelEventArgs>
            (
                handler => (sender, e) => { e.Handled = handled; handler(e); },
                handler => control.PreviewMouseWheel += handler,
                handler => control.PreviewMouseWheel -= handler
            );

        
    }
}
