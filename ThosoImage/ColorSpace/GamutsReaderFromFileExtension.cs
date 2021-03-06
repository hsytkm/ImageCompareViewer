﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace ThosoImage.ColorSpace
{
    public static class GamutsReaderFromFileExtension
    {
        /// <summary>
        /// 引数画像の9分割Gamutを求める
        /// </summary>
        /// <param name="imagePath">対象画像PATH</param>
        /// <returns>9分割Gamut</returns>
        public static Gamut9d Get9DivisionPixelAverage(this string imagePath)
        {
            if (imagePath is null) throw new ArgumentNullException(nameof(imagePath));
            if (!File.Exists(imagePath)) throw new FileNotFoundException(imagePath);
            try
            {
                using (var bitmap = new Bitmap(imagePath))
                {
                    int div = 3;
                    var divWidth = bitmap.Width / div;
                    var divHeight = bitmap.Height / div;

                    // 3で割り切れない場合の補正
                    var marginWidth = bitmap.Width - (divWidth * div);
                    var marginHeight = bitmap.Height - (divHeight * div);

                    var rects = new List<Rectangle>();
                    for (int y = 0; y < div; y++)
                    {
                        for (int x = 0; x < div; x++)
                        {
                            rects.Add(new Rectangle(
                                x * divWidth,
                                y * divHeight,
                                divWidth + ((x == div - 1) ? marginWidth : 0),
                                divHeight + ((y == div - 1) ? marginHeight : 0)));
                        }
                    }

                    var gamuts = bitmap.ReadGamutRgb(rects).ToList();
                    return new Gamut9d(gamuts);
                }
            }
            catch (Exception) { throw; }
        }

        /// <summary>
        /// 49分割の中央部のみ
        /// </summary>
        /// <param name="imagePath">対象画像PATH</param>
        /// <returns>9分割Gamut</returns>
        public static Gamut Get49DivCenterPixelAverage(this string imagePath)
        {
            if (imagePath is null) throw new ArgumentNullException(nameof(imagePath));
            if (!File.Exists(imagePath)) throw new FileNotFoundException(imagePath);
            try
            {
                using (var bitmap = new Bitmap(imagePath))
                {
                    int div = 7;
                    var divWidth = bitmap.Width / div;
                    var divHeight = bitmap.Height / div;

                    var rect = new Rectangle(
                        divWidth * (div / 2),   //Floor
                        divHeight * (div / 2),  //Floor
                        divWidth, divHeight);

                    return bitmap.ReadGamutRgb(rect);
                }
            }
            catch (Exception) { throw; }
        }

    }
}
