/////////////////////////////////////////////////////////////////////////////////////////////////////////////
/// Wrapper for WebP format in C#. (MIT) Jose M. Piñeiro
///////////////////////////////////////////////////////////////////////////////////////////////////////////// 
/// Decode Functions:
/// Bitmap Load(string pathFileName) - Load a WebP file in bitmap.
/// Bitmap Decode(byte[] rawWebP) - Decode WebP data (rawWebP) to bitmap.
/// Bitmap Decode(byte[] rawWebP, WebPDecoderOptions options) - Decode WebP data (rawWebP) to bitmap using 'options'.
/// Bitmap GetThumbnailFast(byte[] rawWebP, int width, int height) - Get a thumbnail from WebP data (rawWebP) with dimensions 'width x height'. Fast mode.
/// Bitmap GetThumbnailQuality(byte[] rawWebP, int width, int height) - Fast get a thumbnail from WebP data (rawWebP) with dimensions 'width x height'. Quality mode.
/// 
/// Encode Functions:
/// Save(Bitmap bmp, string pathFileName, int quality) - Save bitmap with quality lost to WebP file. Opcionally select 'quality'.
/// byte[] EncodeLossy(Bitmap bmp, int quality) - Encode bitmap with quality lost to WebP byte array. Opcionally select 'quality'.
/// byte[] EncodeLossy(Bitmap bmp, int quality, int speed, bool info) - Encode bitmap with quality lost to WebP byte array. Select 'quality', 'speed' and optionally select 'info'.
/// byte[] EncodeLossless(Bitmap bmp) - Encode bitmap without quality lost to WebP byte array. 
/// byte[] EncodeLossless(Bitmap bmp, int speed, bool info = false) - Encode bitmap without quality lost to WebP byte array. Select 'speed'. 
/// byte[] EncodeNearLossless(Bitmap bmp, int quality, int speed = 9, bool info = false) - Encode bitmap with a near lossless method to WebP byte array. Select 'quality', 'speed' and optionally select 'info'.
/// 
/// Another functions:
/// string GetVersion() - Get the library version
/// GetInfo(byte[] rawWebP, out int width, out int height, out bool has_alpha, out bool has_animation, out string format) - Get information of WEBP data
/// float[] PictureDistortion(Bitmap source, Bitmap reference, int metric_type) - Get PSNR, SSIM or LSIM distortion metric between two pictures
/////////////////////////////////////////////////////////////////////////////////////////////////////////////
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security;

namespace WebPWrapper
{
    public sealed class WebP : IDisposable
    {

        #region | Public Decode Functions |
        /// <summary>Decode a WebP image</summary>
        /// <param name="rawWebP">The data to uncompress</param>
        /// <returns>Bitmap with the WebP image</returns>
        public Bitmap Decode(byte[] rawWebP)
        {
            Bitmap bmp = null;
            BitmapData bmpData = null;
            GCHandle pinnedWebP = GCHandle.Alloc(rawWebP, GCHandleType.Pinned);

            try
            {
                //Get image width and height
                GetInfo(rawWebP, out int imgWidth, out int imgHeight, out bool hasAlpha, out bool hasAnimation, out string format);

                //Create a BitmapData and Lock all pixels to be written
                if (hasAlpha)
                    bmp = new Bitmap(imgWidth, imgHeight, PixelFormat.Format32bppArgb);
                else
                    bmp = new Bitmap(imgWidth, imgHeight, PixelFormat.Format24bppRgb);
                bmpData = bmp.LockBits(new Rectangle(0, 0, imgWidth, imgHeight), ImageLockMode.WriteOnly, bmp.PixelFormat);

                //Uncompress the image
                int outputSize = bmpData.Stride * imgHeight;
                IntPtr ptrData = pinnedWebP.AddrOfPinnedObject();
                if (bmp.PixelFormat == PixelFormat.Format24bppRgb)
                     UnsafeNativeMethods.WebPDecodeBGRInto(ptrData, rawWebP.Length, bmpData.Scan0, outputSize, bmpData.Stride);
                else
                     UnsafeNativeMethods.WebPDecodeBGRAInto(ptrData, rawWebP.Length, bmpData.Scan0, outputSize, bmpData.Stride);

                return bmp;
            }
            catch (Exception) { throw; }
            finally
            {
                //Unlock the pixels
                if (bmpData != null)
                    bmp.UnlockBits(bmpData);

                //Free memory
                if (pinnedWebP.IsAllocated)
                    pinnedWebP.Free();
            }
        }

        #endregion

        #region | Another Public Functions |
        /// <summary>Get info of WEBP data</summary>
        /// <param name="rawWebP">The data of WebP</param>
        /// <param name="width">width of image</param>
        /// <param name="height">height of image</param>
        /// <param name="has_alpha">Image has alpha channel</param>
        /// <param name="has_animation">Image is a animation</param>
        /// <param name="format">Format of image: 0 = undefined (/mixed), 1 = lossy, 2 = lossless</param>
        public void GetInfo(byte[] rawWebP, out int width, out int height, out bool has_alpha, out bool has_animation, out string format)
        {
            VP8StatusCode result;
            GCHandle pinnedWebP = GCHandle.Alloc(rawWebP, GCHandleType.Pinned);

            try
            {
                IntPtr ptrRawWebP = pinnedWebP.AddrOfPinnedObject();

                WebPBitstreamFeatures features = new WebPBitstreamFeatures();
                result = UnsafeNativeMethods.WebPGetFeatures(ptrRawWebP, rawWebP.Length, ref features);

                if (result != 0)
                    throw new Exception(result.ToString());

                width = features.Width;
                height = features.Height;
                if (features.Has_alpha == 1) has_alpha = true; else has_alpha = false;
                if (features.Has_animation == 1) has_animation = true; else has_animation = false;
                switch (features.Format)
                {
                    case 1:
                        format = "lossy";
                        break;
                    case 2:
                        format = "lossless";
                        break;
                    default:
                        format = "undefined";
                        break;
                }
            }
            catch (Exception ex) { throw new Exception(ex.Message + "\r\nIn WebP.GetInfo"); }
            finally
            {
                //Free memory
                if (pinnedWebP.IsAllocated)
                    pinnedWebP.Free();
            }
        }
        #endregion

        #region | Destruction |
        /// <summary>Free memory</summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
        #endregion
    }

    #region | Import libwebp functions |
    [SuppressUnmanagedCodeSecurityAttribute]
    internal sealed partial class UnsafeNativeMethods
    {

        private static readonly int WEBP_DECODER_ABI_VERSION = 0x0208;

        /// <summary>Get info of WepP image</summary>
        /// <param name="rawWebP">Bytes[] of WebP image</param>
        /// <param name="data_size">Size of rawWebP</param>
        /// <param name="features">Features of WebP image</param>
        /// <returns>VP8StatusCode</returns>
        internal static VP8StatusCode WebPGetFeatures(IntPtr rawWebP, int data_size, ref WebPBitstreamFeatures features)
        {
            switch (IntPtr.Size)
            {
                case 4:
                    return WebPGetFeaturesInternal_x86(rawWebP, (UIntPtr)data_size, ref features, WEBP_DECODER_ABI_VERSION);
                case 8:
                    return WebPGetFeaturesInternal_x64(rawWebP, (UIntPtr)data_size, ref features, WEBP_DECODER_ABI_VERSION);
                default:
                    throw new InvalidOperationException("Invalid platform. Can not find proper function");
            }
        }
        [DllImport("libwebp_x86.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPGetFeaturesInternal")]
        private static extern VP8StatusCode WebPGetFeaturesInternal_x86([InAttribute()] IntPtr rawWebP, UIntPtr data_size, ref WebPBitstreamFeatures features, int WEBP_DECODER_ABI_VERSION);
        [DllImport("libwebp_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPGetFeaturesInternal")]
        private static extern VP8StatusCode WebPGetFeaturesInternal_x64([InAttribute()] IntPtr rawWebP, UIntPtr data_size, ref WebPBitstreamFeatures features, int WEBP_DECODER_ABI_VERSION);

        /// <summary>Decode WEBP image pointed to by *data and returns BGR samples into a preallocated buffer</summary>
        /// <param name="data">Pointer to WebP image data</param>
        /// <param name="data_size">This is the size of the memory block pointed to by data containing the image data</param>
        /// <param name="output_buffer">Pointer to decoded WebP image</param>
        /// <param name="output_buffer_size">Size of allocated buffer</param>
        /// <param name="output_stride">Specifies the distance between scan lines</param>
        internal static void WebPDecodeBGRInto(IntPtr data, int data_size, IntPtr output_buffer, int output_buffer_size, int output_stride)
        {
            switch (IntPtr.Size)
            {
                case 4:
                    if (WebPDecodeBGRInto_x86(data, (UIntPtr)data_size, output_buffer, output_buffer_size, output_stride) == null)
                        throw new InvalidOperationException("Can not decode WebP");
                    break;
                case 8:
                    if (WebPDecodeBGRInto_x64(data, (UIntPtr)data_size, output_buffer, output_buffer_size, output_stride) == null)
                        throw new InvalidOperationException("Can not decode WebP");
                    break;
                default:
                    throw new InvalidOperationException("Invalid platform. Can not find proper function");
            }
        }
        [DllImport("libwebp_x86.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPDecodeBGRInto")]
        private static extern IntPtr WebPDecodeBGRInto_x86([InAttribute()] IntPtr data, UIntPtr data_size, IntPtr output_buffer, int output_buffer_size, int output_stride);
        [DllImport("libwebp_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPDecodeBGRInto")]
        private static extern IntPtr WebPDecodeBGRInto_x64([InAttribute()] IntPtr data, UIntPtr data_size, IntPtr output_buffer, int output_buffer_size, int output_stride);

        /// <summary>Decode WEBP image pointed to by *data and returns BGRA samples into a preallocated buffer</summary>
        /// <param name="data">Pointer to WebP image data</param>
        /// <param name="data_size">This is the size of the memory block pointed to by data containing the image data</param>
        /// <param name="output_buffer">Pointer to decoded WebP image</param>
        /// <param name="output_buffer_size">Size of allocated buffer</param>
        /// <param name="output_stride">Specifies the distance between scan lines</param>
        internal static void WebPDecodeBGRAInto(IntPtr data, int data_size, IntPtr output_buffer, int output_buffer_size, int output_stride)
        {
            switch (IntPtr.Size)
            {
                case 4:
                    if (WebPDecodeBGRAInto_x86(data, (UIntPtr)data_size, output_buffer, output_buffer_size, output_stride) == null)
                        throw new InvalidOperationException("Can not decode WebP"); 
                    break;
                case 8:
                    if (WebPDecodeBGRAInto_x64(data, (UIntPtr)data_size, output_buffer, output_buffer_size, output_stride) == null)
                        throw new InvalidOperationException("Can not decode WebP");
                    break;
                default:
                    throw new InvalidOperationException("Invalid platform. Can not find proper function");
            }
        }
        [DllImport("libwebp_x86.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPDecodeBGRAInto")]
        private static extern IntPtr WebPDecodeBGRAInto_x86([InAttribute()] IntPtr data, UIntPtr data_size, IntPtr output_buffer, int output_buffer_size, int output_stride);
        [DllImport("libwebp_x64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPDecodeBGRAInto")]
        private static extern IntPtr WebPDecodeBGRAInto_x64([InAttribute()] IntPtr data, UIntPtr data_size, IntPtr output_buffer, int output_buffer_size, int output_stride);
    }
    #endregion

    #region | Predefined |

    /// <summary>Enumeration of the status codes</summary>
    internal enum VP8StatusCode
    {
        /// <summary>No error</summary>
        VP8_STATUS_OK = 0,
        /// <summary>Memory error allocating objects</summary>
        VP8_STATUS_OUT_OF_MEMORY,
        /// <summary>Configuration is invalid</summary>
        VP8_STATUS_INVALID_PARAM,
        VP8_STATUS_BITSTREAM_ERROR,
        /// <summary>Configuration is invalid</summary>
        VP8_STATUS_UNSUPPORTED_FEATURE,
        VP8_STATUS_SUSPENDED,
        /// <summary>Abort request by user</summary>
        VP8_STATUS_USER_ABORT,
        VP8_STATUS_NOT_ENOUGH_DATA,
    }
    #endregion

    #region | libwebp structs |
    /// <summary>Features gathered from the bit stream</summary>
    [StructLayoutAttribute(LayoutKind.Sequential)]
    internal struct WebPBitstreamFeatures
    {
        /// <summary>Width in pixels, as read from the bit stream</summary>
        public int Width;
        /// <summary>Height in pixels, as read from the bit stream</summary>
        public int Height;
        /// <summary>True if the bit stream contains an alpha channel</summary>
        public int Has_alpha;
        /// <summary>True if the bit stream is an animation</summary>
        public int Has_animation;
        /// <summary>0 = undefined (/mixed), 1 = lossy, 2 = lossless</summary>
        public int Format;
        /// <summary>Padding for later use</summary>
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 5, ArraySubType = UnmanagedType.U4)]
        private readonly uint[] pad;
    };
    #endregion
}