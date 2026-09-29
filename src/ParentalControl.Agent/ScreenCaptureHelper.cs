using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.IO;

namespace ParentalControl.Agent;

public static class ScreenCaptureHelper
{
    public static byte[]? CaptureScreenJpeg(int maxWidth, long quality, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            var bounds = SystemInformation.VirtualScreen;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return null;
            }

            using var fullBmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(fullBmp))
            {
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            }

            Bitmap finalBmp;
            bool isScaled = false;

            if (bounds.Width > maxWidth)
            {
                var newWidth = maxWidth;
                var newHeight = (int)Math.Max(1, Math.Round((double)bounds.Height * maxWidth / bounds.Width));
                finalBmp = new Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb);
                using (var gScaled = Graphics.FromImage(finalBmp))
                {
                    gScaled.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    gScaled.SmoothingMode = SmoothingMode.HighQuality;
                    gScaled.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    gScaled.DrawImage(fullBmp, 0, 0, newWidth, newHeight);
                }
                isScaled = true;
            }
            else
            {
                finalBmp = new Bitmap(fullBmp.Width, fullBmp.Height, PixelFormat.Format24bppRgb);
                using (var g24 = Graphics.FromImage(finalBmp))
                {
                    g24.DrawImage(fullBmp, 0, 0);
                }
                isScaled = true;
            }

            width = finalBmp.Width;
            height = finalBmp.Height;

            using var ms = new MemoryStream();
            var encoder = GetEncoder(ImageFormat.Jpeg);
            if (encoder != null)
            {
                using var encParams = new EncoderParameters(1);
                encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                finalBmp.Save(ms, encoder, encParams);
            }
            else
            {
                finalBmp.Save(ms, ImageFormat.Jpeg);
            }

            if (isScaled)
            {
                finalBmp.Dispose();
            }

            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        return codecs.FirstOrDefault(codec => codec.FormatID == format.Guid);
    }
}
