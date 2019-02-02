﻿using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace ThosoImage.Wpf.Imaging
{
    public static class BitmapImageFromFile
    {
        /// <summary>
        /// ファイルからBitmapImageを元画像のサイズ読み出す
        /// </summary>
        /// <param name="imagePath">ファイルPATH</param>
        /// <param name="isCanGC">メモリ不足時のGC実行可否</param>
        /// <returns>BitmapImage</returns>
        public static BitmapImage ToBitmapImage(this string imagePath, bool isCanGC = true)
        {
            return ToBitmapImage(imagePath, null, null, isCanGC);
        }

        /// <summary>
        /// ファイルからBitmapImageを指定サイズで読み出す
        /// </summary>
        /// <param name="imagePath">ファイルPATH</param>
        /// <param name="width">読み込み後の画像幅(nullなら元画像サイズ)</param>
        /// <param name="height">読み込み後の画像高さ(nullなら元画像サイズ)</param>
        /// <param name="isCanGC">メモリ不足時のGC実行可否</param>
        /// <returns>BitmapImage</returns>
        public static BitmapImage ToBitmapImage(this string imagePath, int? width, int? height, bool isCanGC = true)
        {
            if (imagePath is null) throw new ArgumentNullException();
            if (!File.Exists(imagePath)) throw new FileNotFoundException();

            var bi = new BitmapImage();
            try
            {
                // アプリが画像ファイルを占有しない
                using (var fs = File.Open(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bi.BeginInit();
                    if (width.HasValue) bi.DecodePixelWidth = width.Value;
                    if (height.HasValue) bi.DecodePixelHeight = height.Value;
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bi.StreamSource = fs;
                    bi.EndInit();
                }
                if (bi.Width == 1 && bi.Height == 1) throw new OutOfMemoryException();
            }
            catch (OutOfMemoryException ex)
            {
                Debug.WriteLine($"{ex} ({Path.GetFileName(imagePath)})");

                // メモリリーク時はGCしてみる(画像表示されない現象の低減)
                // https://stackoverflow.com/questions/50040087/c-sharp-bitmapimage-width-and-height-equal-1
                if (isCanGC)
                {
                    GC.Collect();                           // アクセス不可能なオブジェクトを除去
                    GC.WaitForPendingFinalizers();          // ファイナライゼーションが終わるまでスレッド待機
                    GC.Collect();                           // ファイナライズされたばかりのオブジェクトに関連するメモリを開放
                    bi = ToBitmapImage(imagePath, false);   // GC禁止でコール
                }
            }
            finally
            {
                bi.Freeze();
            }
            return bi;
        }
    }
}