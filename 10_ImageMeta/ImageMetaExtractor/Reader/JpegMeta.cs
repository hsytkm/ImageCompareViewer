﻿using ImageMetaExtractor.Common;
using System;

namespace ImageMetaExtractor.Reader
{
    class JpegMeta : ImageMetaExtractorExifBase
    {
        private const string TagName = "JPEG";

        private enum META_TAG_ID : int
        {
            IMAGE_WIDTH = 0x0003,
            IMAGE_HEIGHT = 0x0001,
        };

        // 何度も取得するのでコンストラクタで用意する
        private readonly int _width;
        private readonly int _height;

        public JpegMeta(string imagePath) : base(imagePath)
        {
            var directory = GetDirectory(TagName);
            if (directory != null)
            {
                _width = directory.GetTagValue<UInt16>((int)META_TAG_ID.IMAGE_WIDTH);
                _height = directory.GetTagValue<UInt16>((int)META_TAG_ID.IMAGE_HEIGHT);
            }
        }

        internal override int GetImageWidth() => _width;
        internal override int GetImageHeight() => _height;

    }
}
