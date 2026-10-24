using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

// ReSharper disable once CheckNamespace
namespace DlibDotNet.Extensions
{

    public static class BitmapExtensions
    {

        #region Fields

        private static readonly Dictionary<SKColorType, int> OptimumChannels;

        private static readonly Dictionary<SKColorType, ConvertInfo<ImageTypes>[]> OptimumConvertImageInfos;

        private static readonly Dictionary<SKColorType, ConvertInfo<MatrixElementTypes>> OptimumConvertMatrixInfos;

        #endregion

        #region Constructors

        static BitmapExtensions()
        {
            OptimumChannels = new Dictionary<SKColorType, int>();
            OptimumChannels[SKColorType.Gray8] = 1;
            OptimumChannels[SKColorType.Bgra8888] =
            OptimumChannels[SKColorType.Rgba8888] =
            OptimumChannels[SKColorType.Rgb888x] = 4;

            // SkiaSharp has no 24bpp color type, so 24bpp images are represented as BGRA8888 (B,G,R,A byte order),
            // which keeps the same B,G,R channel order as the former GDI+ Format24bppRgb.

            OptimumConvertImageInfos = new Dictionary<SKColorType, ConvertInfo<ImageTypes>[]>();
            OptimumConvertImageInfos[SKColorType.Gray8] = new[]
            {
                new ConvertInfo<ImageTypes> { Type = ImageTypes.UInt8 }
            };
            OptimumConvertImageInfos[SKColorType.Bgra8888] = new[]
            {
                new ConvertInfo<ImageTypes>{ Type = ImageTypes.RgbPixel, RgbReverse = true },
                new ConvertInfo<ImageTypes>{ Type = ImageTypes.BgrPixel, RgbReverse = false }
            };
            OptimumConvertImageInfos[SKColorType.Rgba8888] = new[]
            {
                new ConvertInfo<ImageTypes>{ Type = ImageTypes.RgbPixel, RgbReverse = false },
                new ConvertInfo<ImageTypes>{ Type = ImageTypes.BgrPixel, RgbReverse = true }
            };
            OptimumConvertImageInfos[SKColorType.Rgb888x] = new[]
            {
                new ConvertInfo<ImageTypes>{ Type = ImageTypes.RgbPixel, RgbReverse = false },
                new ConvertInfo<ImageTypes>{ Type = ImageTypes.BgrPixel, RgbReverse = true }
            };
            OptimumConvertMatrixInfos = new Dictionary<SKColorType, ConvertInfo<MatrixElementTypes>>();
            OptimumConvertMatrixInfos[SKColorType.Gray8] = new ConvertInfo<MatrixElementTypes> { Type = MatrixElementTypes.UInt8 };
            OptimumConvertMatrixInfos[SKColorType.Bgra8888] = new ConvertInfo<MatrixElementTypes> { Type = MatrixElementTypes.RgbPixel, RgbReverse = true };
            OptimumConvertMatrixInfos[SKColorType.Rgba8888] = new ConvertInfo<MatrixElementTypes> { Type = MatrixElementTypes.RgbPixel, RgbReverse = false };
            OptimumConvertMatrixInfos[SKColorType.Rgb888x] = new ConvertInfo<MatrixElementTypes> { Type = MatrixElementTypes.RgbPixel, RgbReverse = false };
        }

        #endregion

        #region Methods

        #region Array2D

        public static SKBitmap ToBitmap<T>(this Array2D<T> array)
            where T : struct
        {
            array.ThrowIfDisposed();

            var width = array.Columns;
            var height = array.Rows;
            var channels = 4;
            var rgbReverse = false;

            switch (array.ImageType)
            {
                case ImageTypes.RgbPixel:
                    // Dlib RgbPixel data
                    // R,G,B,R,G,B,...
                    // But SKBitmap BGRA8888 data
                    // B,G,R,B,G,R,...
                    rgbReverse = true;
                    break;
                case ImageTypes.BgrPixel:
                    // Dlib BgrPixel data
                    // B,G,R,B,G,R,...
                    // SKBitmap BGRA8888 data
                    // B,G,R,B,G,R,...
                    break;
                case ImageTypes.RgbAlphaPixel:
                    // Dlib RgbAlphaPixel data
                    // R,G,B,A,R,G,B,A,...
                    // But SKBitmap BGRA8888 data
                    // B,G,R,A,B,G,R,A,...
                    rgbReverse = true;
                    break;
                case ImageTypes.UInt8:
                case ImageTypes.Int32:
                case ImageTypes.UInt16:
                case ImageTypes.Int16:
                case ImageTypes.HsiPixel:
                case ImageTypes.LabPixel:
                case ImageTypes.Float:
                case ImageTypes.Double:
                case ImageTypes.Matrix:
                    throw new NotSupportedException();
            }

            var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            // Erase with opaque black so alpha bytes not written by the converter are 0xFF
            bitmap.Erase(SKColors.Black);
            ToManaged(array.ImageType, array.NativePtr, bitmap, rgbReverse, channels);
            return bitmap;
        }

        public static Array2D<T> ToArray2D<T>(this SKBitmap bitmap)
            where T : struct
        {
            var format = bitmap.ColorType;
            if (!OptimumConvertImageInfos.TryGetValue(format, out var infos))
                throw new NotSupportedException($"{format} is not support");
            if (!OptimumChannels.TryGetValue(format, out var channels))
                throw new NotSupportedException($"{format} is not support");

            var width = bitmap.Width;
            var height = bitmap.Height;
            var requireDispose = true;

            Array2D<T> array = null;

            try
            {
                array = new Array2D<T>(height, width);
                var info = infos.FirstOrDefault(i => i.Type == array.ImageType);
                if (info != null)
                {
                    ToNative(bitmap, info.Type, array.NativePtr, info.RgbReverse, channels);
                    requireDispose = false;
                }
                else
                {
                    throw new NotSupportedException($"Not support converting from {format} to {array.ImageType}");
                }
            }
            finally
            {
                if (requireDispose)
                {
                    array?.Dispose();
                    throw new NotSupportedException();
                }
            }

            return array;
        }

        #endregion

        #region Bitmap

        /// <summary>
        /// Converts this <see cref="SKBitmap"/> to an 8bpp grayscale <see cref="SKBitmap"/>.
        /// </summary>
        /// <remarks>
        /// SkiaSharp does not provide an indexed (palettized) color type, so the returned
        /// <see cref="SKBitmap"/> uses <see cref="SKColorType.Gray8"/>, which is equivalent
        /// to the former 8bpp indexed grayscale bitmap with a 256-entry gray palette.
        /// </remarks>
        public static SKBitmap To8bppIndexedGrayscale(this SKBitmap bitmap, GrayscalLumaCoefficients coefficients)
        {
            float red;
            float green;
            float blue;

            switch (coefficients)
            {
                case GrayscalLumaCoefficients.ITU_R_BT_601:
                    red = 0.299F;
                    green = 0.587F;
                    blue = 0.1144F;
                    break;
                case GrayscalLumaCoefficients.ITU_R_BT_709:
                    red = 0.2126F;
                    green = 0.7152F;
                    blue = 0.0722F;
                    break;
                case GrayscalLumaCoefficients.SMPTE_240M:
                    red = 0.212F;
                    green = 0.701F;
                    blue = 0.087F;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(coefficients), coefficients, null);
            }

            return To8bppIndexedGrayscale(bitmap, red, green, blue);
        }

        public static SKBitmap To8bppIndexedGrayscale(this SKBitmap bitmap, float rCoefficient, float gCoefficient, float bCoefficient)
        {
            var format = bitmap.ColorType;
            if (format != SKColorType.Gray8 &&
                format != SKColorType.Bgra8888 &&
                format != SKColorType.Rgba8888 &&
                format != SKColorType.Rgb888x)
                throw new NotSupportedException($"{format} is not support");

            if (!OptimumChannels.TryGetValue(format, out var channels))
                throw new NotSupportedException($"{format} is not support");

            var width = bitmap.Width;
            var height = bitmap.Height;

            SKBitmap dst = null;

            try
            {
                dst = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);

                unsafe
                {
                    var srcBase = (byte*)bitmap.GetPixels();
                    var srcStride = bitmap.RowBytes;
                    var dstBase = (byte*)dst.GetPixels();
                    var dstStride = dst.RowBytes;

                    switch (channels)
                    {
                        case 1:
                            for (var y = 0; y < height; y++)
                                NativeMethods.cstd_memcpy((IntPtr)(dstBase + y * dstStride), (IntPtr)(srcBase + y * srcStride), width);
                            break;
                        case 4:
                            {
                                // The former GDI+ code assumed B,G,R byte order (Format24bppRgb / Format32bppRgb).
                                // Keep that semantics for Bgra8888; Rgba8888 / Rgb888x store R,G,B instead.
                                var bgrOrder = format == SKColorType.Bgra8888;

                                for (var y = 0; y < height; y++)
                                {
                                    var pSrc = srcBase + y * srcStride;
                                    var pDst = dstBase + y * dstStride;
                                    for (var x = 0; x < width; x++, pSrc += channels, pDst++)
                                    {
                                        if (bgrOrder)
                                            *pDst = (byte)(pSrc[0] * bCoefficient + pSrc[1] * gCoefficient + pSrc[2] * rCoefficient);
                                        else
                                            *pDst = (byte)(pSrc[0] * rCoefficient + pSrc[1] * gCoefficient + pSrc[2] * bCoefficient);
                                    }
                                }
                            }

                            break;
                    }
                }

                return dst;
            }
            catch
            {
                dst?.Dispose();
                throw;
            }
        }

        #endregion

        #region Matrix

        public static SKBitmap ToBitmap<T>(this Matrix<T> matrix)
            where T : struct
        {
            matrix.ThrowIfDisposed();

            var width = matrix.Columns;
            var height = matrix.Rows;
            int channels = 4;
            var rgbReverse = false;

            switch (matrix.MatrixElementType)
            {
                case MatrixElementTypes.RgbPixel:
                    rgbReverse = true;
                    break;
                case MatrixElementTypes.BgrPixel:
                    break;
                case MatrixElementTypes.RgbAlphaPixel:
                    rgbReverse = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            // Erase with opaque black so alpha bytes not written by the converter are 0xFF
            bitmap.Erase(SKColors.Black);
            ToManaged(matrix.MatrixElementType, matrix.NativePtr, bitmap, rgbReverse, channels);
            return bitmap;
        }

        public static Matrix<T> ToMatrix<T>(this SKBitmap bitmap)
            where T : struct
        {
            var format = bitmap.ColorType;
            if (!OptimumConvertMatrixInfos.TryGetValue(format, out var info))
                throw new NotSupportedException($"{format} is not support");
            if (!OptimumChannels.TryGetValue(format, out var channels))
                throw new NotSupportedException($"{format} is not support");

            var width = bitmap.Width;
            var height = bitmap.Height;
            var requireDispose = true;

            Matrix<T> matrix = null;

            try
            {
                matrix = new Matrix<T>(height, width);
                if (matrix.MatrixElementType == info.Type)
                {
                    ToNative(bitmap, info.Type, matrix.NativePtr, info.RgbReverse, channels);
                    requireDispose = false;
                }
            }
            finally
            {
                if (requireDispose)
                {
                    matrix?.Dispose();
                    throw new NotSupportedException();
                }
            }

            return matrix;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Gets the native array2d type used by the native extension converter for the specified image type.
        /// </summary>
        /// <remarks>
        /// dlib::<see cref="BgrPixel"/> and dlib::rgb_pixel are both 3-byte pixels with identical layout.
        /// The native converter only implements the rgb_pixel path, so <see cref="BgrPixel"/> is routed
        /// through it with the channel order adjusted by <paramref name="rgbReverse"/>.
        /// </remarks>
        private static NativeMethods.Array2DType ToNativeArray2DTypeForConversion(ImageTypes type)
        {
            if (type == ImageTypes.BgrPixel)
                return ImageTypes.RgbPixel.ToNativeArray2DType();
            return type.ToNativeArray2DType();
        }

        /// <summary>
        /// Gets the native matrix element type used by the native extension converter for the specified element type.
        /// </summary>
        /// <remarks>
        /// dlib::<see cref="BgrPixel"/> and dlib::rgb_pixel are both 3-byte pixels with identical layout.
        /// The native converter only implements the rgb_pixel path, so <see cref="BgrPixel"/> is routed
        /// through it with the channel order adjusted by <paramref name="rgbReverse"/>.
        /// </remarks>
        private static NativeMethods.MatrixElementType ToNativeMatrixElementTypeForConversion(MatrixElementTypes type)
        {
            if (type == MatrixElementTypes.BgrPixel)
                return MatrixElementTypes.RgbPixel.ToNativeMatrixElementType();
            return type.ToNativeMatrixElementType();
        }

        private static void ToManaged(ImageTypes type, IntPtr src, SKBitmap bitmap, bool rgbReverse, int channels)
        {
            var width = bitmap.Width;
            var height = bitmap.Height;
            var dst = bitmap.GetPixels();
            var stride = bitmap.RowBytes;
            var srcType = ToNativeArray2DTypeForConversion(type);
            NativeMethods.extensions_convert_array_to_managed_image(srcType, src, dst, rgbReverse, (uint)height, (uint)width, (uint)stride, (uint)channels);
        }

        private static void ToManaged(MatrixElementTypes type, IntPtr src, SKBitmap bitmap, bool rgbReverse, int channels)
        {
            var width = bitmap.Width;
            var height = bitmap.Height;
            var dst = bitmap.GetPixels();
            var stride = bitmap.RowBytes;
            var srcType = ToNativeMatrixElementTypeForConversion(type);
            NativeMethods.extensions_convert_matrix_to_managed_image(srcType, src, dst, rgbReverse, (uint)height, (uint)width, (uint)stride, (uint)channels);
        }

        private static void ToNative(SKBitmap bitmap, ImageTypes dstType, IntPtr dst, bool rgbReverse, int channels)
        {
            var width = bitmap.Width;
            var height = bitmap.Height;
            var src = bitmap.GetPixels();
            var stride = bitmap.RowBytes;
            var dstNativeType = ToNativeArray2DTypeForConversion(dstType);
            NativeMethods.extensions_convert_managed_image_to_array(src, dstNativeType, dst, rgbReverse, (uint)height, (uint)width, (uint)stride, (uint)channels);
        }

        private static void ToNative(SKBitmap bitmap, MatrixElementTypes dstType, IntPtr dst, bool rgbReverse, int channels)
        {
            var width = bitmap.Width;
            var height = bitmap.Height;
            var src = bitmap.GetPixels();
            var stride = bitmap.RowBytes;
            var dstNativeType = ToNativeMatrixElementTypeForConversion(dstType);
            NativeMethods.extensions_convert_managed_image_to_matrix(src, dstNativeType, dst, rgbReverse, (uint)height, (uint)width, (uint)stride, (uint)channels);
        }

        #endregion

        #endregion

    }

    internal sealed class ConvertInfo<T>
    {

        public T Type
        {
            get;
            set;
        }

        public bool RgbReverse
        {
            get;
            set;
        }

    }

}
