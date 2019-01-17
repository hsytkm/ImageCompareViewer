﻿namespace ImagePixels.Common
{
    static class Gamut
    {
        public static double GetY(double R, double G, double B)
        {
            return 0.299 * R + 0.587 * G + 0.114 * B;
        }

    }
}
