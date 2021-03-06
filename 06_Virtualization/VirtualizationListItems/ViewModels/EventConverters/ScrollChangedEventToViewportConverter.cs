﻿using System;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using Reactive.Bindings.Interactivity;

namespace VirtualizationListItems.ViewModels.EventConverters
{
    /// <summary>
    /// ScrollChangedイベント
    /// </summary>
    public class ScrollChangedEventToViewportConverter : ReactiveConverter<dynamic, (double CenterRatio, double ViewportRatio)>
    {
        protected override IObservable<(double CenterRatio, double ViewportRatio)> OnConvert(IObservable<dynamic> source)
        {
            return source
                .Cast<ScrollChangedEventArgs>()
                //.Where(e => !(e.ViewportWidthChange == 0 && e.ViewportHeightChange == 0) || e.HorizontalChange != 0)
                .Select(e => (CenterRatio: CenterRatio(e.ExtentWidth, e.ViewportWidth, e.HorizontalOffset),
                    ViewportRatio: ViewportRatio(e.ExtentWidth, e.ViewportWidth)));
        }

        /// <summary>
        /// 表示範囲の中央の割合
        /// </summary>
        private static double CenterRatio(double length, double viewport, double offset)
        {
            if (length == 0) return 0;
            return ClipRatio((offset + (viewport / 2)) / length);
        }

        /// <summary>
        /// 全要素と表示範囲の割合(要素が全て表示されていたら1.0)
        /// </summary>
        private static double ViewportRatio(double length, double viewport)
        {
            if (length == 0) return 0;
            return ClipRatio(viewport / length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double ClipRatio(double value) =>
            (value <= 0) ? 0 : (1 < value ? 1 : value);

    }
}
