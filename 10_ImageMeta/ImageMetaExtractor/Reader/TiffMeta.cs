﻿using ImageMetaExtractor.Common;
using System;

namespace ImageMetaExtractor.Reader
{
    class TiffMeta : ImageMetaExtractorBase
    {
        private static readonly string TagName = ExifProperties.MainTagName;

        // 何度も取得するのでコンストラクタで用意する
        private readonly int _width;
        private readonly int _height;

        public TiffMeta(string imagePath) : base(imagePath)
        {
            var directory = GetDirectory(TagName);
            if (directory != null)
            {
                _width = (int)directory.GetTagValue<uint>((int)ExifProperties.EXIF_MAIN_ID.IMAGE_WIDTH);
                _height = (int)directory.GetTagValue<uint>((int)ExifProperties.EXIF_MAIN_ID.IMAGE_HEIGHT);
            }
        }

        internal override int GetImageWidth() => _width;
        internal override int GetImageHeight() => _height;

    }
}
