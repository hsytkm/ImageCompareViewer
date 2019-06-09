﻿using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ZoomThumb.ViewModels;
using ZoomThumb.Views.Common;

namespace ZoomThumb.Views.Controls
{
    // 尾上さんのお言葉
    // Viewに特別な要件が存在しない限りは、トリガーやアクションの自作にこだわらず積極的にコードビハインドを使いましょう
    // Viewのコードビハインドは、基本的にView内で完結するロジックとViewModelからのイベントの受信(専用リスナを使用する)に限るとトラブルが少なくなります

    /// <summary>
    /// ScrollImageViewer.xaml の相互作用ロジック
    /// </summary>
    public partial class ScrollImageViewer : UserControl
    {
        private static readonly Type SelfType = typeof(ScrollImageViewer);

        #region InterlockedData

        private class UniqueId
        {
            private static int BaseId = 0;  // 1からID割り振られる
            public int Id { get; }

            // newする度に一意IDをインクリ
            public UniqueId() => Id = Interlocked.Increment(ref BaseId);
        }
        private readonly UniqueId MyInstanceId = new UniqueId();

        private struct InterlockedData<T>
        {
            public int PublisherId { get; }
            public T Data { get; }
            public InterlockedData(int id, T data)
            {
                PublisherId = id;
                Data = data;
            }
        }

        #endregion

        #region InterlockedField

        // staticにより全インスタンスでズーム倍率を共有する
        private static readonly ReactivePropertySlim<InterlockedData<ImageZoomMagnification>> InterlockedImageZoomMag =
            new ReactivePropertySlim<InterlockedData<ImageZoomMagnification>>(mode: ReactivePropertyMode.None);

        private void PulishInterlockedImageZoomMag(ImageZoomMagnification mag) =>
            InterlockedImageZoomMag.Value = new InterlockedData<ImageZoomMagnification>(MyInstanceId.Id, mag);

        // staticにより全インスタンスでスクロールシフト量を共有する
        private static readonly ReactivePropertySlim<InterlockedData<Vector>> InterlockedImageScrollVectorRatio =
            new ReactivePropertySlim<InterlockedData<Vector>>(mode: ReactivePropertyMode.None);

        private void PulishInterlockedScrollVectorRatio(Vector vector) =>
            InterlockedImageScrollVectorRatio.Value = new InterlockedData<Vector>(MyInstanceId.Id, vector);

        #endregion

        #region ScrollOffsetRequest

        // スクロールオフセットの移動通知
        private struct ScrollOffsetRequest
        {
            public readonly Point CenterRatio;
            public readonly Vector VectorRatio;

            private ScrollOffsetRequest(Point p, Vector v)
            {
                double clip(double value, double min, double max) => (value <= min) ? min : ((value >= max) ? max : value);

                if (0 <= p.X && p.X <= 1 && 0 <= p.Y && p.Y <= 1)
                {
                    CenterRatio = p;
                }
                else
                {
                    CenterRatio = new Point(clip(p.X, 0, 1), clip(p.Y, 0, 1));
                }
                VectorRatio = v;
            }

            public static ScrollOffsetRequest GetDefaultInstance() =>
                GetInstance(new Point(0.5, 0.5));

            public static ScrollOffsetRequest GetInstance(Point newPoint) =>
                new ScrollOffsetRequest(newPoint, new Vector(0.0, 0.0));

            public static ScrollOffsetRequest GetInstanceWithInterlocked(Point oldPoint, Point newPoint) =>
                new ScrollOffsetRequest(newPoint, newPoint - oldPoint);

            public static ScrollOffsetRequest GetInstanceWithInterlocked(Point oldPoint, Vector vector) =>
                new ScrollOffsetRequest(oldPoint + vector, vector);
        }

        #endregion

        // ズーム倍率(内部イベント用)
        private readonly ReactivePropertySlim<ImageZoomMagnification> ImageZoomMag = new ReactivePropertySlim<ImageZoomMagnification>(ImageZoomMagnification.Entire);
        private readonly ReactivePropertySlim<ScrollOffsetRequest> ImageScrollOffsetRatio =
            new ReactivePropertySlim<ScrollOffsetRequest>(ScrollOffsetRequest.GetDefaultInstance());

        // スクロールバー除いた領域のコントロール（全画面でバーが消えた後にサイズ更新するために必要）
        private readonly ReactivePropertySlim<Size> ScrollContentActualSize = new ReactivePropertySlim<Size>(mode: ReactivePropertyMode.DistinctUntilChanged);
        private readonly ReactivePropertySlim<Unit> ScrollContentMouseLeftDown = new ReactivePropertySlim<Unit>(mode: ReactivePropertyMode.None);
        private readonly ReactivePropertySlim<Unit> ScrollContentMouseLeftUp = new ReactivePropertySlim<Unit>(mode: ReactivePropertyMode.None);
        private readonly ReactivePropertySlim<Point> ScrollContentMouseMove = new ReactivePropertySlim<Point>(mode: ReactivePropertyMode.DistinctUntilChanged);

        // 画像コントロール
        private readonly ReactivePropertySlim<Size> ImageSourcePixelSize = new ReactivePropertySlim<Size>(mode: ReactivePropertyMode.DistinctUntilChanged);
        private readonly ReactivePropertySlim<Size> ImageViewActualSize = new ReactivePropertySlim<Size>(mode: ReactivePropertyMode.DistinctUntilChanged);
        private readonly ReactivePropertySlim<int> MouseWheelZoomDelta = new ReactivePropertySlim<int>(mode: ReactivePropertyMode.None);

        #region ZoomPayloadProperty(TwoWay)

        // 画像ズーム倍率
        private static readonly DependencyProperty ZoomPayloadProperty =
            DependencyProperty.Register(
                nameof(ZoomPayload),
                typeof(ImageZoomPayload),
                SelfType,
                new FrameworkPropertyMetadata(
                    default(ImageZoomPayload),
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    (d, e) =>
                    {
                        if (e.NewValue is ImageZoomPayload payload)
                        {
                            var siViewer = ViewHelper.GetChildControl<ScrollImageViewer>(d);
                            if (siViewer != null)
                            {
                                siViewer.ImageZoomMag.Value = new ImageZoomMagnification(payload);
                            }
                        }
                    }));

        public ImageZoomPayload ZoomPayload
        {
            get => (ImageZoomPayload)GetValue(ZoomPayloadProperty);
            set => SetValue(ZoomPayloadProperty, value);
        }

        #endregion

        #region ScrollOffsetCenterRatioProperty(TwoWay)

        // スクロールバー中央の絶対位置の割合(0~1)
        private static readonly DependencyProperty ScrollOffsetCenterRatioProperty =
            DependencyProperty.Register(
                nameof(ScrollOffsetCenterRatio),
                typeof(Point),
                SelfType,
                new FrameworkPropertyMetadata(
                    default(Point),
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    (d, e) =>
                    {
                        if (e.OldValue is Point oldPoint && e.NewValue is Point newPoint)
                        {
                            var siViewer = ViewHelper.GetChildControl<ScrollImageViewer>(d);
                            if (siViewer != null)
                            {
                                siViewer.ImageScrollOffsetRatio.Value = ScrollOffsetRequest.GetInstance(newPoint);
                            }
                        }
                    }));

        public Point ScrollOffsetCenterRatio
        {
            get => (Point)GetValue(ScrollOffsetCenterRatioProperty);
            set => SetValue(ScrollOffsetCenterRatioProperty, value);
        }

        #endregion

        #region ImageSourceProperty(OneWay)

        // ソース画像
        private static readonly DependencyProperty ImageSourceProperty =
            DependencyProperty.Register(
                nameof(ImageSource),
                typeof(ImageSource),
                SelfType,
                new FrameworkPropertyMetadata(
                    default(ImageSource),
                    FrameworkPropertyMetadataOptions.None));

        public ImageSource ImageSource
        {
            get => (ImageSource)GetValue(ImageSourceProperty);
            set => SetValue(ImageSourceProperty, value);
        }

        #endregion

        #region ScrollOffsetVectorRatioProperty(OneWay)

        // スクロールバーのシフト量の割合(0~1)
        private static readonly DependencyProperty ScrollOffsetVectorRatioProperty =
            DependencyProperty.Register(
                nameof(ScrollOffsetVectorRatio),
                typeof(Vector),
                SelfType,
                new FrameworkPropertyMetadata(
                    default(Vector),
                    FrameworkPropertyMetadataOptions.None,
                    (d, e) =>
                    {
                        if (e.NewValue is Vector vector)
                        {
                            var siViewer = ViewHelper.GetChildControl<ScrollImageViewer>(d);
                            if (siViewer != null)
                            {
                                siViewer.ImageScrollOffsetRatio.Value =
                                    ScrollOffsetRequest.GetInstanceWithInterlocked(siViewer.ImageScrollOffsetRatio.Value.CenterRatio, vector);
                            }
                        }
                    }));

        public Vector ScrollOffsetVectorRatio
        {
            get => (Vector)GetValue(ScrollOffsetVectorRatioProperty);
            set => SetValue(ScrollOffsetVectorRatioProperty, value);
        }

        #endregion

        #region IsViewerInterlockProperty(OneWay)

        // スクロール/ズーム倍率を他コントロールに連動
        private static readonly DependencyProperty IsViewerInterlockProperty =
            DependencyProperty.Register(
                nameof(IsViewerInterlock),
                typeof(bool),
                SelfType,
                new FrameworkPropertyMetadata(
                    default(bool),
                    FrameworkPropertyMetadataOptions.None));

        public bool IsViewerInterlock
        {
            get => (bool)GetValue(IsViewerInterlockProperty);
            set => SetValue(IsViewerInterlockProperty, value);
        }

        #endregion

        #region IsVisibleReducedImageProperty(OneWayToSource)

        // 縮小画像の表示切り替えフラグ(画像の全体表示中は非表示)
        private static readonly DependencyProperty IsVisibleReducedImageProperty =
            DependencyProperty.Register(
                nameof(IsVisibleReducedImage),
                typeof(bool),
                SelfType,
                new FrameworkPropertyMetadata(
                    false,
                    FrameworkPropertyMetadataOptions.None));

        public bool IsVisibleReducedImage
        {
            get => (bool)GetValue(IsVisibleReducedImageProperty);
            set => SetValue(IsVisibleReducedImageProperty, value);
        }

        #endregion

        #region ImageCursorPointProperty(OneWayToSource)

        // View画像上のカーソル位置(画像Pixel座標系)
        private static readonly DependencyProperty ImageCursorPointProperty =
            DependencyProperty.Register(
                nameof(ImageCursorPoint),
                typeof(Point),
                SelfType,
                new FrameworkPropertyMetadata(
                    default(Point),
                    FrameworkPropertyMetadataOptions.None));

        public Point ImageCursorPoint
        {
            get => (Point)GetValue(ImageCursorPointProperty);
            set => SetValue(ImageCursorPointProperty, value);
        }

        #endregion

        public ScrollImageViewer()
        {
            InitializeComponent();

            #region EventHandler

            MainScrollViewer.ScrollChanged += new ScrollChangedEventHandler(ScrollImageViewer_ScrollChanged);

            this.Loaded += (_, __) =>
            {
                var scrollContentPresenter = ViewHelper.GetChildControl<ScrollContentPresenter>(this);
                if (scrollContentPresenter != null)
                {
                    scrollContentPresenter.PreviewMouseLeftButtonDown += (sender, e) => ScrollContentMouseLeftDown.Value = Unit.Default;
                    scrollContentPresenter.PreviewMouseLeftButtonUp += (sender, e) => ScrollContentMouseLeftUp.Value = Unit.Default;
                    scrollContentPresenter.MouseMove += (sender, e) => ScrollContentMouseMove.Value = e.GetPosition((IInputElement)sender);

                    // 初期サイズはLoadedで取得しようとしたけどイベント来ないのでココで
                    //scrollContentPresenter.Loaded += (sender, e) => 
                    ScrollContentActualSize.Value = ViewHelper.GetControlActualSize(scrollContentPresenter);
                    scrollContentPresenter.SizeChanged += (sender, e) =>
                    {
                        ScrollContentActualSize.Value = e.NewSize; //=ActualSize
                    };
                }

                MainImage.TargetUpdated += (sender, e) =>
                {
                    if (!(e.OriginalSource is Image image)) return;
                    ImageSourcePixelSize.Value = ViewHelper.GetImageSourcePixelSize(image);
                };
                MainImage.SizeChanged += (sender, e) =>
                {
                    ImageViewActualSize.Value = e.NewSize; //=ActualSize
                    MainImage_SizeChanged(sender, e);
                };
                MainImage.MouseMove += (sender, e) =>
                {
                    double clip(double value, double min, double max) => (value <= min) ? min : ((value >= max) ? max : value);

                    // 原画像のピクセル位置を返す
                    if (ImageViewActualSize.Value.IsValidValue())
                    {
                        var cursorPoint = e.GetPosition((IInputElement)sender);
                        var x = Math.Round(cursorPoint.X * ImageSourcePixelSize.Value.Width / ImageViewActualSize.Value.Width);
                        x = clip(x, 0, ImageSourcePixelSize.Value.Width - 1);
                        var y = Math.Round(cursorPoint.Y * ImageSourcePixelSize.Value.Height / ImageViewActualSize.Value.Height);
                        y = clip(y, 0, ImageSourcePixelSize.Value.Height - 1);
                        ImageCursorPoint = new Point(x, y);
                    }
                };

                if (VisualTreeHelper.GetParent(this) is Panel parentPanel)
                {
                    // サムネイルコントロール(Canvas)でもズーム操作を有効にするため、親パネルに添付イベントを貼る
                    parentPanel.PreviewMouseWheel += (sender, e) =>
                    {
                        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                        {
                            MouseWheelZoomDelta.Value = e.Delta;

                            // 最大ズームでホイールすると画像の表示エリアが移動しちゃうので止める
                            e.Handled = true;
                        }
                    };
                }
            };

            #endregion

            #region ImageZoomMag

            // ズーム倍率変更
            ImageZoomMag.CombineLatest(ImageSourcePixelSize, ScrollContentActualSize,
                    (mag, imageSourceSize, scrollContentSize) => (mag, imageSourceSize, scrollContentSize))
                .Subscribe(x =>
                {
                    // 他インスタンスとズーム倍率を連動させる
                    if (IsViewerInterlock)
                    {
                        PulishInterlockedImageZoomMag(x.mag);
                    }

                    UpdateImageZoom(x.mag, x.imageSourceSize, x.scrollContentSize);
                });

            InterlockedImageZoomMag
                .Where(x => x.PublisherId != MyInstanceId.Id)
                .Select(x => x.Data)
                .Subscribe(mag => UpdateImageZoom(mag, ImageSourcePixelSize.Value, ScrollContentActualSize.Value));

            #endregion

            #region DoubleClick

            // ダブルクリックイベントの自作 http://y-maeyama.hatenablog.com/entry/20110313/1300002095
            // ScrollViewerのMouseDoubleClickだとScrollBarのDoubleClickも拾ってしまうので
            // またScrollContentPresenterにMouseDoubleClickイベントは存在しない
            var preDoubleClick = ScrollContentMouseLeftDown
                .Select(_ => (Time: DateTime.Now, Point: ScrollContentMouseMove.Value))
                //.Do(x => Console.WriteLine($"SingleClick: {x.Time} {x.Point.X} x {x.Point.Y}"))
                .Pairwise()
                .Where(x => x.NewItem.Time.Subtract(x.OldItem.Time) <= TimeSpan.FromMilliseconds(500))
                // 高速に画像シフトするとダブルクリック判定されるのでマウス位置が動いていないことを見る
                .Where(x => Math.Abs(x.NewItem.Point.X - x.OldItem.Point.X) <= 3)
                .Where(x => Math.Abs(x.NewItem.Point.Y - x.OldItem.Point.Y) <= 3)
                .Select(_ => DateTime.Now)
                .ToReadOnlyReactivePropertySlim(mode: ReactivePropertyMode.RaiseLatestValueOnSubscribe);

            // ダブルクリックの2回が、3クリック目で2度目のダブルクリックになる対策
            // (ダブルクリック後に一定時間が経過するまでダブルクリックを採用しない)
            var scrollContentDoubleClick = preDoubleClick
                .Pairwise()
                //.Do(x => Console.WriteLine($"PreDoubleClick2 {x.OldItem.ToString("HH:mm:ss.fff")}  {x.NewItem.ToString("HH:mm:ss.fff")}"))
                .Where(x => x.NewItem.Subtract(x.OldItem) >= TimeSpan.FromMilliseconds(200))
                .ToUnit()
                .ToReadOnlyReactivePropertySlim(mode: ReactivePropertyMode.None);

            scrollContentDoubleClick.Subscribe(_ => SwitchClickZoomMag());

            #endregion

            #region SingleClickZoom

            // 一時ズームフラグ
            var temporaryZoom = new ReactivePropertySlim<bool>(false, mode: ReactivePropertyMode.DistinctUntilChanged);

            // 長押しによる一時ズーム
            // Rx入門 (15) - スケジューラの利用 https://blog.xin9le.net/entry/2012/01/24/120722
            ScrollContentMouseLeftDown
                .Throttle(TimeSpan.FromMilliseconds(300))       // 長押し判定
                .TakeUntil(ScrollContentMouseLeftUp)            // 押下中のみ対象(ちょん離し後なら弾く)
                //.Do(x => Console.Write($"ThreadId: {Thread.CurrentThread.ManagedThreadId}"))
                .ObserveOnUIDispatcher()                        // 以降はUIスレッドに同期
                //.Do(x => Console.WriteLine($" -> {Thread.CurrentThread.ManagedThreadId}"))
                .Repeat()
                .Where(_ => ImageZoomMag.Value.IsEntire)        // 全体表示なら流す(継続ズームを弾くため既にズームしてたら流さない)
                .Subscribe(_ => temporaryZoom.Value = true);

            // 一時ズーム解除
            ScrollContentMouseLeftUp
                .Where(_ => temporaryZoom.Value)                // 一時ズームなら解除する(継続ズームは解除しない)
                .Subscribe(_ => temporaryZoom.Value = false);

            temporaryZoom.Subscribe(_ => SwitchClickZoomMag());

            #endregion

            #region MouseWheelZoom

            // マウスホイールによるズーム倍率変更
            MouseWheelZoomDelta
                .Where(x => x != 0)
                .Select(x => x > 0)
                .Subscribe(isZoomIn =>
                {
                    var oldImageZoomMag = ImageZoomMag.Value;

                    // ズーム前の倍率
                    double oldZoomMagRatio = GetCurrentZoomMagRatio(ImageViewActualSize.Value, ImageSourcePixelSize.Value);

                    // ズーム後のズーム管理クラス
                    var newImageZoomMag = oldImageZoomMag.ZoomMagnification(oldZoomMagRatio, isZoomIn);

                    // 全画面表示時を跨ぐ場合は全画面表示にする
                    var enrireZoomMag = GetEntireZoomMagRatio(ScrollContentActualSize.Value, ImageSourcePixelSize.Value);

                    if ((oldImageZoomMag.MagnificationRatio < enrireZoomMag && enrireZoomMag < newImageZoomMag.MagnificationRatio)
                        || (newImageZoomMag.MagnificationRatio < enrireZoomMag && enrireZoomMag < oldImageZoomMag.MagnificationRatio))
                    {
                        newImageZoomMag = new ImageZoomMagnification(true, enrireZoomMag);
                    }
                    ImageZoomMag.Value = newImageZoomMag;
                });

            #endregion

            #region ScrollOffset

            // 標準スクロールバー操作による移動
            ImageScrollOffsetRatio
                .CombineLatest(ScrollContentActualSize, ImageViewActualSize,
                    (offset, sview, iview) => (offset, sview, iview))
                .Subscribe(x =>
                {
                    double clip(double value, double min, double max) => (value <= min) ? min : ((value >= max) ? max : value);

                    var scrollViewer = MainScrollViewer;

                    // 好き勝手に要求された位置を範囲制限する
                    var rateRange = GetScrollOffsetRateRange(scrollViewer);
                    var newOffset = new Point(
                        clip(x.offset.CenterRatio.X, rateRange.widthMin, rateRange.widthMax),
                        clip(x.offset.CenterRatio.Y, rateRange.heightMin, rateRange.heightMax));

                    var sviewHalf = new Size(x.sview.Width / 2.0, x.sview.Height / 2.0);
                    if (!sviewHalf.IsValidValue()) return;

                    var horiOffset = Math.Max(0.0, newOffset.X * x.iview.Width - sviewHalf.Width);
                    var vertOffset = Math.Max(0.0, newOffset.Y * x.iview.Height - sviewHalf.Height);

                    scrollViewer.ScrollToHorizontalOffset(horiOffset);
                    scrollViewer.ScrollToVerticalOffset(vertOffset);

                    // 他インスタンスとスクロールのシフト量を連動させる
                    if (IsViewerInterlock)
                    {
                        PulishInterlockedScrollVectorRatio(x.offset.VectorRatio);
                    }

                    // ズーム倍率管理プロパティの更新
                    ScrollOffsetCenterRatio = newOffset;
                });

            // インスタンス間の移動量の共有
            InterlockedImageScrollVectorRatio
                .Where(x => x.PublisherId != MyInstanceId.Id)
                .Select(x => x.Data)
                .Subscribe(vector => ImageScrollOffsetRatio.Value = ScrollOffsetRequest.GetInstance(ImageScrollOffsetRatio.Value.CenterRatio + vector));

            #endregion

            #region MouseDragScroll

            // 画像ドラッグによるスクロールバーの移動
            ScrollContentMouseMove
                .Pairwise()                                 // 最新値と前回値を取得
                .Select(x => -(x.NewItem - x.OldItem))      // 引っ張りと逆方向なので反転
                .SkipUntil(ScrollContentMouseLeftDown)
                .TakeUntil(ScrollContentMouseLeftUp)
                .Repeat()
                .Where(_ => !ImageZoomMag.Value.IsEntire)   // ズーム中のみ流す(全画面表示中は画像移動不要)
                .Where(_ => !temporaryZoom.Value)           // ◆一時ズームは移動させない仕様
                .Subscribe(vector =>
                {
                    if (!ImageViewActualSize.Value.IsValidValue()) return;

                    // マウスドラッグ中の表示位置のシフト
                    var vecRatio = new Vector(
                        vector.X / ImageViewActualSize.Value.Width,
                        vector.Y / ImageViewActualSize.Value.Height);

                    ImageScrollOffsetRatio.Value =
                        ScrollOffsetRequest.GetInstanceWithInterlocked(ImageScrollOffsetRatio.Value.CenterRatio, vecRatio);
                });

            #endregion

        }

        #region ImageZoomMag

        private void UpdateImageZoom(ImageZoomMagnification zoomMagnification, Size imageSourceSize, Size scrollPresenterSize)
        {
            if (!imageSourceSize.IsValidValue()) return;

            var scrollViewer = MainScrollViewer;
            var image = MainImage;

            // 画像サイズの更新前にスクロールバーの表示を更新(ContentSizeに影響出るので)
            UpdateScrollBarVisibility(scrollViewer, zoomMagnification, scrollPresenterSize, imageSourceSize);

            if (!zoomMagnification.IsEntire)
            {
                // ズーム表示に切り替え
                image.Width = imageSourceSize.Width * zoomMagnification.MagnificationRatio;
                image.Height = imageSourceSize.Height * zoomMagnification.MagnificationRatio;
            }
            else
            {
                // 全画面表示に切り替え
                var size = GetEntireZoomSize(scrollPresenterSize, imageSourceSize);
                image.Width = size.Width;
                image.Height = size.Height;
            }
            ImageZoomMag.Value = zoomMagnification;
        }

        private static void UpdateScrollBarVisibility(ScrollViewer scrollViewer, ImageZoomMagnification zoomMag, Size sviewSize, Size sourceSize)
        {
            var visible = ScrollBarVisibility.Hidden;

            // ズームインならスクロールバーを表示
            if (!zoomMag.IsEntire && (GetEntireZoomMagRatio(sviewSize, sourceSize) < zoomMag.MagnificationRatio))
                visible = ScrollBarVisibility.Visible;

            scrollViewer.HorizontalScrollBarVisibility =
            scrollViewer.VerticalScrollBarVisibility = visible;
        }

        #endregion

        #region ZoomSize

        // 全画面表示のサイズを取得
        private static Size GetEntireZoomSize(Size sviewSize, Size sourceSize)
        {
            if (!sourceSize.Height.IsValidValue()) return default;
            var imageRatio = sourceSize.Width / sourceSize.Height;

            double width, height;

            if (!sviewSize.Height.IsValidValue()) return default;

            if (imageRatio > sviewSize.Width / sviewSize.Height)
            {
                width = sviewSize.Width;      // 横パンパン
                height = sviewSize.Width / imageRatio;
            }
            else
            {
                width = sviewSize.Height * imageRatio;
                height = sviewSize.Height;    // 縦パンパン
            }
            return new Size(width, height);
        }

        // 全画面表示のズーム倍率を取得
        private static double GetEntireZoomMagRatio(Size sviewSize, Size sourceSize) =>
            GetZoomMagRatio(GetEntireZoomSize(sviewSize, sourceSize), sourceSize);

        // 現在のズーム倍率を取得
        private static double GetCurrentZoomMagRatio(Size imageViewSize, Size imageSourceSize) =>
            GetZoomMagRatio(imageViewSize, imageSourceSize);

        // 引数サイズのズーム倍率を求める
        private static double GetZoomMagRatio(Size newSize, Size baseSize) =>
            Math.Min(newSize.Width / baseSize.Width, newSize.Height / baseSize.Height);

        #endregion

        #region ImageSizeChanged

        // 画像のサイズ変更(ズーム操作)
        private void MainImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var imageViewActualSize = e.NewSize;

            // 全画面表示よりもズームしてるかフラグ(e.NewSize == Size of MainImage)
            // 小数点以下がちょいずれして意図通りの判定にならないことがあるので整数化する
            bool isZoomOverEntire = (Math.Floor(imageViewActualSize.Width) > Math.Floor(ScrollContentActualSize.Value.Width)
                || Math.Floor(imageViewActualSize.Height) > Math.Floor(ScrollContentActualSize.Value.Height));

            // 全画面よりズームインしてたら縮小画像を表示
            IsVisibleReducedImage = isZoomOverEntire;

            // 全画面よりズームアウトしたらスクロールバー位置を初期化
            if (!isZoomOverEntire)
            {
                ImageScrollOffsetRatio.Value = ScrollOffsetRequest.GetDefaultInstance();
            }

            // ズーム倍率プロパティの更新
            var magRatio = GetCurrentZoomMagRatio(imageViewActualSize, ImageSourcePixelSize.Value);
            ZoomPayload = new ImageZoomPayload(ImageZoomMag.Value.IsEntire, magRatio);
        }

        #endregion

        #region SwitchClickZoomMag

        // クリックズームの状態を切り替える(全画面⇔ズーム)
        private void SwitchClickZoomMag()
        {
            if (!ImageZoomMag.Value.IsEntire)
            {
                // ここで倍率詰めるのは無理(コントロールサイズが変わっていないため)
                ImageZoomMag.Value = ImageZoomMagnification.Entire; // ToAll
            }
            else
            {
                ImageZoomMag.Value = ImageZoomMagnification.MagX1;  // ToZoom

                // ズーム表示への切り替えならスクロールバーを移動(ImageViewSizeを変更した後に実施する)
                if (ImageViewActualSize.Value.IsValidValue())
                {
                    double clip(double value, double min, double max) => (value <= min) ? min : ((value >= max) ? max : value);

                    // 親ScrollViewerから子Imageまでのサイズ
                    var imageControlSizeOffset = new Size(
                        Math.Max(0.0, ScrollContentActualSize.Value.Width - ImageViewActualSize.Value.Width) / 2.0,
                        Math.Max(0.0, ScrollContentActualSize.Value.Height - ImageViewActualSize.Value.Height) / 2.0);

                    // 子Image基準のマウス位置
                    var mousePos = new Point(
                        Math.Max(0.0, ScrollContentMouseMove.Value.X - imageControlSizeOffset.Width),
                        Math.Max(0.0, ScrollContentMouseMove.Value.Y - imageControlSizeOffset.Height));

                    // ズーム後の中心座標の割合
                    var newPoint = new Point(
                        clip(mousePos.X / ImageViewActualSize.Value.Width, 0.0, 1.0),
                        clip(mousePos.Y / ImageViewActualSize.Value.Height, 0.0, 1.0));

                    // 全画面から絶対スクロールオフセットに移動
                    ImageScrollOffsetRatio.Value =
                        ScrollOffsetRequest.GetInstanceWithInterlocked(ImageScrollOffsetRatio.Value.CenterRatio, newPoint);
                }
            }
        }

        #endregion

        #region ScrollChanged

        private void ScrollImageViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (ImageViewActualSize.Value.IsValidValue())
            {
                var newPoint = new Point(
                    (e.HorizontalOffset + e.ViewportWidth / 2.0) / ImageViewActualSize.Value.Width,
                    (e.VerticalOffset + e.ViewportHeight / 2.0) / ImageViewActualSize.Value.Height);
                var scrollOffset = ScrollOffsetRequest.GetInstanceWithInterlocked(ImageScrollOffsetRatio.Value.CenterRatio, newPoint);

                // 全体表示なら中央位置を上書き
                if (e.ViewportWidth == e.ExtentWidth || e.ViewportHeight == e.ExtentHeight)
                {
                    scrollOffset = ScrollOffsetRequest.GetDefaultInstance();
                }

                ImageScrollOffsetRatio.Value = scrollOffset;
            }
        }

        // スクロールバー位置の範囲(割合)を取得
        private static (double widthMin, double widthMax, double heightMin, double heightMax)
            GetScrollOffsetRateRange(ScrollViewer sView)
        {
            (double, double, double, double) nolimit = (0.0, 1.0, 0.0, 1.0);

            // 全体表示ならオフセットに制限なし
            if (sView.ExtentWidth < sView.ViewportWidth || sView.ExtentHeight < sView.ViewportHeight)
            {
                return nolimit;
            }
            else if (sView.ExtentWidth.IsValidValue() && sView.ExtentHeight.IsValidValue())
            {
                var widthRateMin = (sView.ViewportWidth / 2.0) / sView.ExtentWidth;
                var widthRateMax = (sView.ExtentWidth - sView.ViewportWidth / 2.0) / sView.ExtentWidth;
                var heightRateMin = (sView.ViewportHeight / 2.0) / sView.ExtentHeight;
                var heightRateMax = (sView.ExtentHeight - sView.ViewportHeight / 2.0) / sView.ExtentHeight;
                return (widthRateMin, widthRateMax, heightRateMin, heightRateMax);
            }
            return nolimit;
        }

        #endregion

    }
}
