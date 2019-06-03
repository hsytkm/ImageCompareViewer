﻿using Prism.Ioc;
using Prism.Regions;
using System.Windows;

namespace ZoomThumb.Views
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow(IContainerExtension container, IRegionManager regionManager)
        {
            InitializeComponent();

            int count = 2;
            for (int i = 0; i < count; i++)
            {
                regionManager.RegisterViewWithRegion(
                    $"ImageScrollControl{i}",
                    () => container.Resolve<ImageScrollControl>());
            }
        }
    }
}
