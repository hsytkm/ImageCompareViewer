﻿using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using System;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interactivity;
using System.Windows.Media;
using ZoomThumb.Common;
using ZoomThumb.Views.Common;

namespace ZoomThumb.Views.Behaviors
{
    class MovableFrameBehavior : Behavior<FrameworkElement>
    {
        private static readonly Type SelfType = typeof(MovableFrameBehavior);

        #region InterlockedField

        private readonly UniqueId MyInstanceId = new UniqueId();

        // staticにより全インスタンスで枠位置のシフト量を共有する
        private static readonly ReactivePropertySlim<InterlockedData<Vector>> InterlockedPointVectorRatio =
            new ReactivePropertySlim<InterlockedData<Vector>>(mode: ReactivePropertyMode.None);

        private void PulishInterlockedPointShiftRatio(Vector vector) =>
            InterlockedPointVectorRatio.Value = new InterlockedData<Vector>(MyInstanceId.Id, vector);

        // staticにより全インスタンスで枠サイズを共有する
        private static readonly ReactivePropertySlim<InterlockedData<Size>> InterlockedNewSizeRatio =
            new ReactivePropertySlim<InterlockedData<Size>>(mode: ReactivePropertyMode.None);

        private void PulishInterlockedSizeChangeRatio(Size size) =>
            InterlockedNewSizeRatio.Value = new InterlockedData<Size>(MyInstanceId.Id, size);

        #endregion

        private static bool IsSizeChanging => (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        private static readonly double DefaultLengthRatio = 0.1;
        private static readonly double DefaultAddrRatio = 0.5 - (DefaultLengthRatio / 2.0);
        private static readonly Size DefaultSizeRatio = new Size(DefaultLengthRatio, DefaultLengthRatio);
        private static readonly Point DefaultPointRatio = new Point(DefaultAddrRatio, DefaultAddrRatio);

        private readonly MyCompositeDisposable CompositeDisposable = new MyCompositeDisposable();

        #region FrameRectRatioProperty(TwoWay)

        // View枠の割合
        private static readonly DependencyProperty FrameRectRatioProperty =
            DependencyProperty.Register(
                nameof(FrameRectRatio),
                typeof(Rect),
                SelfType,
                new FrameworkPropertyMetadata(
                    default(Rect),
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    (d, e) =>
                    {
                        if (d is MovableFrameBehavior behavior && e.NewValue is Rect rectRatio)
                        {
                            //behavior.FrameAddrSizeRatio.Value = rectRatio;
                        }
                    }));

        public Rect FrameRectRatio
        {
            get => (Rect)GetValue(FrameRectRatioProperty);
            set => SetValue(FrameRectRatioProperty, value);
        }

        #endregion

        #region IsFrameInterlockProperty(OneWay)

        // スクロール/サイズを他コントロールと連動
        private static readonly DependencyProperty IsFrameInterlockProperty =
            DependencyProperty.Register(
                nameof(IsFrameInterlock),
                typeof(bool),
                SelfType,
                new FrameworkPropertyMetadata(
                    false,
                    FrameworkPropertyMetadataOptions.None));

        public bool IsFrameInterlock
        {
            get => (bool)GetValue(IsFrameInterlockProperty);
            set => SetValue(IsFrameInterlockProperty, value);
        }

        #endregion

        protected override void OnAttached()
        {
            base.OnAttached();

            var parentPanel = VisualTreeHelper.GetParent(AssociatedObject) as Panel;
            if (parentPanel is null) throw new ResourceReferenceKeyNotFoundException();

            // マウスポインタ変更(サイズ変更:斜め両矢印 / 位置変更:両矢印の十字)
            AssociatedObject.MouseMoveAsObservable()
                .Subscribe(_ => Window.GetWindow(AssociatedObject).Cursor = IsSizeChanging ? Cursors.SizeNWSE : Cursors.SizeAll)
                .AddTo(CompositeDisposable);

            // マウスポインタを通常(左上向き矢印)に戻す
            AssociatedObject.MouseLeaveAsObservable()
                .Subscribe(_ => Window.GetWindow(AssociatedObject).Cursor = Cursors.Arrow)
                .AddTo(CompositeDisposable);

            // 親パネルのサイズ取得
            var groundPanelSize = parentPanel.SizeChangedAsObservable().Select(e => e.NewSize)
                .ToReadOnlyReactivePropertySlim(mode: ReactivePropertyMode.DistinctUntilChanged)
                .AddTo(CompositeDisposable);

            // マウスクリック移動による枠位置の更新
            var mouseDragPoint = AssociatedObject.MouseLeftDragPointAsObservable(originControl: parentPanel, handled: true);

            #region サイズ変更イベント

            var frameNewSizeRatio = new ReactivePropertySlim<Size>(DefaultSizeRatio).AddTo(CompositeDisposable);

            // サイズ変更イベント
            mouseDragPoint
                .Where(_ => IsSizeChanging)
                .CombineLatest(groundPanelSize, (dragPoint, groundSize) => (dragPoint, groundSize))
                .Subscribe(x =>
                {
                    // 枠現在位置とマウス現在位置の差分を新サイズにする
                    var dragPointRatio = new Point(x.dragPoint.X / x.groundSize.Width, x.dragPoint.Y / x.groundSize.Height);
                    var frameSizeRatio = GetNewSizeRatio(dragPointRatio, FrameRectRatio);

                    // 他コントロールへの通知
                    if (IsFrameInterlock) PulishInterlockedSizeChangeRatio(frameSizeRatio);

                    // 自コントロールの更新
                    frameNewSizeRatio.Value = frameSizeRatio;
                })
                .AddTo(CompositeDisposable);

            // インスタンス間のサイズの共有
            InterlockedNewSizeRatio
                .Where(x => x.PublisherId != MyInstanceId.Id)
                .Select(x => x.Data)
                .Subscribe(x => frameNewSizeRatio.Value = x)
                .AddTo(CompositeDisposable);

            // 自コントロールのサイズ
            frameNewSizeRatio
                .CombineLatest(groundPanelSize, (newSizeRatio, groundSize) => (newSizeRatio, groundSize))
                .Subscribe(x => UpdateFrameView(x.groundSize, x.newSizeRatio))
                .AddTo(CompositeDisposable);

            #endregion

            #region 位置変更イベント

            var frameNewPointRatio = new ReactivePropertySlim<Point>(DefaultPointRatio).AddTo(CompositeDisposable);

            //◆◆ シフトが変！ ◆◆

            // 位置変更イベント
            mouseDragPoint
                .Where(_ => !IsSizeChanging)
                .Pairwise()
                .Select(x => x.NewItem - x.OldItem)
                .CombineLatest(groundPanelSize, (dragVector, groundSize) => (dragVector, groundSize))
                .Subscribe(x =>
                {
                    // ドラッグ移動量から新位置を取得する
                    var dragVectorRatio = new Vector(x.dragVector.X / x.groundSize.Width, x.dragVector.Y / x.groundSize.Height);
                    var framePointRatio = GetNewPointRatio(dragVectorRatio, FrameRectRatio);

                    // 他コントロールへの通知
                    if (IsFrameInterlock)
                    {
                        PulishInterlockedPointShiftRatio(dragVectorRatio);
                    }

                    // 自コントロールの更新
                    frameNewPointRatio.Value = framePointRatio;
                })
                .AddTo(CompositeDisposable);

            // インスタンス間のシフト量の共有
            InterlockedPointVectorRatio
                .Where(x => x.PublisherId != MyInstanceId.Id)
                .Select(x => x.Data)
                .Subscribe(x => frameNewPointRatio.Value = FrameRectRatio.TopLeft + x)
                .AddTo(CompositeDisposable);

            // 自コントロールの位置
            frameNewPointRatio
                .CombineLatest(groundPanelSize, (newPointRatio, groundSize) => (newPointRatio, groundSize))
                .Subscribe(x => UpdateFrameView(x.groundSize, x.newPointRatio))
                .AddTo(CompositeDisposable);

            #endregion

        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            CompositeDisposable.Dispose();
        }

        #region GetNewSize/Point

        // 枠の新サイズを取得
        private static Size GetNewSizeRatio(Point dragPointRatio, Rect currentFrameRatio)
        {
            double clip(double value, double min, double max) => (value <= min) ? min : ((value >= max) ? max : value);

            // 現在位置とドラッグ位置から新サイズを求める
            var frameSizeRatio = new Size(Math.Max(0, dragPointRatio.X - currentFrameRatio.Left), Math.Max(0, dragPointRatio.Y - currentFrameRatio.Top));

            frameSizeRatio.Width = clip(frameSizeRatio.Width, 0, 1 - currentFrameRatio.Left);
            frameSizeRatio.Height = clip(frameSizeRatio.Height, 0, 1 - currentFrameRatio.Top);
            return frameSizeRatio;
        }

        // 枠の新位置を取得
        private static Point GetNewPointRatio(Vector dragVectorRatio, Rect currentFrameRatio)
        {
            double clip(double value, double min, double max) => (value <= min) ? min : ((value >= max) ? max : value);

            // 現在位置とドラッグ移動量から新位置を求める
            var framePointRatio = currentFrameRatio.TopLeft + dragVectorRatio;

            framePointRatio.X = clip(framePointRatio.X, 0, 1 - currentFrameRatio.Width);
            framePointRatio.Y = clip(framePointRatio.Y, 0, 1 - currentFrameRatio.Height);
            return framePointRatio;
        }

        #endregion

        #region UpdateFrameView

        // サイズのみ変更
        private void UpdateFrameView(Size groundSize, Size sizeRatio) =>
            UpdateFrameView(groundSize, FrameRectRatio.TopLeft, sizeRatio);

        // 位置のみ変更
        private void UpdateFrameView(Size groundSize, Point pointRatio) =>
            UpdateFrameView(groundSize, pointRatio, FrameRectRatio.Size);

        // サイズと位置の変更
        private void UpdateFrameView(Size groundSize, Point pointRatio, Size sizeRatio)
        {
            double clip(double value, double min, double max) => (value <= min) ? min : ((value >= max) ? max : value);

            var rect = new Rect(pointRatio, sizeRatio);
            rect.Scale(groundSize.Width, groundSize.Height);

            var width = clip(rect.Width, 0.0, groundSize.Width);
            var height = clip(rect.Height, 0.0, groundSize.Height);
            var left = clip(rect.Left, 0.0, groundSize.Width - width);
            var top = clip(rect.Top, 0.0, groundSize.Height - height);

            var fe = AssociatedObject;
            Canvas.SetLeft(fe, left);
            Canvas.SetTop(fe, top);
            fe.Width = width;
            fe.Height = height;

            // プロパティの更新
            FrameRectRatio = new Rect(
                left / groundSize.Width, top / groundSize.Height,
                width / groundSize.Width, height / groundSize.Height);
        }

        #endregion

    }
}
