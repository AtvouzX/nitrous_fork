using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Ui;
using Xunit;
using Application = System.Windows.Application;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;
using DrawingPoint = System.Drawing.Point;
using DrawingRect = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace Nitrous.Tests;

/// <summary>
/// Thorough rendering coverage for every surface a user sees (OSD, Dashboard,
/// Fan Curve) across the screen matrix users actually run: common resolutions,
/// portrait/ultrawide, offset multi-monitor working areas, and Windows DPI scales.
/// WPF surfaces render to RenderTargetBitmap and are checked at pixel level for
/// blank/transparent/clipped output; the WinForms OSD renders off-screen frames.
/// Placement is asserted through the pure helpers for the full cross-product.
/// </summary>
public class ScreenRenderingMatrixTests
{
    private static readonly (int W, int H)[] Resolutions =
    [
        (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (1920, 1200),
        (2560, 1080), (2560, 1440), (3440, 1440), (3840, 2160), (1080, 1920)
    ];

    private static readonly double[] Scales = [1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0];

    private static IEnumerable<DrawingRect> WorkingAreas(int w, int h)
    {
        yield return new DrawingRect(0, 0, w, h - 48);        // bottom taskbar, primary
        yield return new DrawingRect(w, 0, w, h - 48);        // secondary right of primary
        yield return new DrawingRect(-w, 0, w, h - 48);       // secondary left (negative origin)
        yield return new DrawingRect(0, 48, w, h - 48);       // top taskbar
        yield return new DrawingRect(64, 0, w - 64, h);       // left taskbar
    }

    // ------------------------------------------------------------------ runners

    private static void RunInSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null) new Application();
                action();
            }
            catch (Exception e) { ex = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (ex != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
    }

    private static void RunInWinFormsSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { ex = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (ex != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
    }

    // ------------------------------------------------------------- placement

    [Fact]
    public void AllSurfaces_StayInsideWorkingArea_AcrossScreenMatrix()
    {
        foreach (var (resW, resH) in Resolutions)
        foreach (double scale in Scales)
        foreach (var area in WorkingAreas(resW, resH))
        {
            string ctx = $"{resW}x{resH} @{scale}x area={area}";

            // OSD (physical pixels)
            var osd = OsdLayoutHelper.Compute(scale, (int)(160 * scale));
            var osdPos = OsdLayoutHelper.ComputePosition(osd.FormSize, area, osd.BottomMargin);
            AssertInside(osdPos.X, osdPos.Y, osd.FormSize.Width, osd.FormSize.Height, area, $"OSD {ctx}");
            if (osd.FormSize.Width <= area.Width)
            {
                int expectedX = area.Left + (area.Width - osd.FormSize.Width) / 2;
                Assert.True(Math.Abs(osdPos.X - expectedX) <= 1, $"OSD not centered: {ctx}");
            }

            // Dashboard dock (device pixels = logical * scale)
            double dashW = 375 * scale, dashH = 580 * scale;
            var (dashLeft, dashTop) = WindowPlacementHelper.ComputeDashboardDock(
                area.Left, area.Top, area.Right, area.Bottom, dashW, dashH);
            AssertInside(dashLeft, dashTop, dashW, dashH, area, $"Dashboard {ctx}");

            // Fan Curve editor placed relative to the docked dashboard
            double curveW = 880 * scale, curveH = 660 * scale;
            var (curveLeft, curveTop) = WindowPlacementHelper.ComputeCurveEditorPosition(
                dashLeft, dashTop, dashW, dashH, curveW, curveH,
                area.Left, area.Top, area.Right, area.Bottom);
            AssertInside(curveLeft, curveTop, curveW, curveH, area, $"FanCurve {ctx}");
        }
    }

    private static void AssertInside(double x, double y, double w, double h, DrawingRect area, string ctx)
    {
        Assert.True(x >= area.Left, $"Left outside: {ctx} x={x}");
        Assert.True(y >= area.Top, $"Top outside: {ctx} y={y}");
        if (w <= area.Width) Assert.True(x + w <= area.Right, $"Right overflow: {ctx}");
        else Assert.Equal(area.Left, (int)x);
        if (h <= area.Height) Assert.True(y + h <= area.Bottom, $"Bottom overflow: {ctx}");
        else Assert.Equal(area.Top, (int)y);
    }

    [Fact]
    public void OsdLayout_InteriorRectsStayInBounds_AcrossScales()
    {
        foreach (double scale in Scales)
        foreach (int content in new[] { 0, 120, 350, 5000 })
        {
            var layout = OsdLayoutHelper.Compute(scale, content);
            Assert.True(layout.IconBounds.Left >= 0 && layout.IconBounds.Right <= layout.FormSize.Width, $"Icon out of bounds @{scale}x content={content}");
            Assert.True(layout.DividerRect.Left >= layout.IconBounds.Right, $"Divider overlaps icon @{scale}x");
            Assert.True(layout.TextRect.Left >= layout.DividerRect.Right, $"Text overlaps divider @{scale}x");
            Assert.True(layout.TextRect.Right <= layout.FormSize.Width, $"Text overflow @{scale}x");
            Assert.True(layout.Chamfer < layout.FormSize.Height / 2, $"Chamfer too large @{scale}x");
        }
    }

    // ------------------------------------------------------------------- OSD

    public static IEnumerable<object[]> OsdRenderCases()
    {
        foreach (double scale in Scales)
        foreach (var profile in new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Turbo })
            yield return new object[] { scale, profile };
    }

    [Theory]
    [MemberData(nameof(OsdRenderCases))]
    public void Osd_RendersNonBlankFrame_WithTransparentRight_AcrossDpiAndProfiles(double scale, PowerProfile profile)
    {
        RunInWinFormsSta(() =>
        {
            using var form = new OsdForm();
            using var bitmap = form.RenderFrameForTesting(scale, profile);
            var minLayout = OsdLayoutHelper.Compute(scale, 0);
            var maxLayout = OsdLayoutHelper.Compute(scale, 5000);

            Assert.True(bitmap.Width >= minLayout.FormSize.Width && bitmap.Width <= maxLayout.FormSize.Width
                && bitmap.Height == minLayout.FormSize.Height,
                $"Unexpected frame size {bitmap.Width}x{bitmap.Height} @{scale}x");
            Assert.True(bitmap.Width >= 250, $"Frame too narrow @{scale}x");

            // Chamfered corners cut away
            Assert.Equal(0, bitmap.GetPixel(0, 0).A);
            Assert.Equal(0, bitmap.GetPixel(bitmap.Width - 1, 0).A);

            // Gradient: left dense/dark, right faded (desktop shows through)
            int sampleY = Math.Max(2, bitmap.Height / 6);
            var left = bitmap.GetPixel(bitmap.Width / 12, sampleY);
            var right = bitmap.GetPixel(bitmap.Width * 11 / 12, sampleY);
            Assert.True(left.A > right.A + 80, $"Gradient not faded right @{scale}x {profile}: left A={left.A} right A={right.A}");

            var stats = AnalyzeGdi(bitmap);
            Assert.True(stats.OpaqueFraction > 0.2, $"Frame mostly transparent @{scale}x {profile}: {stats.OpaqueFraction:F2}");
            Assert.True(stats.DistinctColors > 20, $"Frame near-blank @{scale}x {profile}: {stats.DistinctColors} colors");

            // Profile accent painted inside the icon region
            var accent = OsdThemeHelper.GetAccent(profile);
            int iconAccent = CountNearColor(bitmap,
                new DrawingRect(bitmap.Width / 20, bitmap.Height / 4, bitmap.Width / 6, bitmap.Height / 2), accent, 90);
            Assert.True(iconAccent >= 8, $"Accent missing in icon @{scale}x {profile}: {iconAccent}px");

            // White title/subtitle glyphs painted in the text region
            int textWhite = CountNearColor(bitmap,
                new DrawingRect(bitmap.Width * 3 / 10, bitmap.Height / 5, bitmap.Width * 13 / 20, bitmap.Height * 3 / 5),
                Color.White, 120);
            Assert.True(textWhite >= 15, $"Text missing @{scale}x {profile}: {textWhite}px");
        });
    }

    // -------------------------------------------------------------- Dashboard

    [Theory]
    [InlineData(375, 580, 1.0)]
    [InlineData(375, 580, 1.25)]
    [InlineData(375, 580, 1.5)]
    [InlineData(375, 580, 2.0)]
    [InlineData(320, 480, 1.5)]   // snapped / small screen stress
    [InlineData(500, 700, 1.25)]  // enlarged stress
    public void Dashboard_RendersNonBlankFrame_AcrossSizesAndDpi(double w, double h, double scale)
    {
        RunInSta(() =>
        {
            var window = new NitrousDashboard();
            try
            {
                window.Show();
                window.Width = w;
                window.Height = h;
                window.UpdateLayout();
                window.UpdateChamferGeometry();
                // Bare STA test threads never run the window layout queue;
                // arrange the page directly (same pattern as DashboardResolutionMatrixTests).
                window.WindowRoot.Measure(new WpfSize(w, h));
                window.WindowRoot.Arrange(new Rect(0, 0, w, h));
                window.DashPage.Measure(new WpfSize(w, h - 86));
                window.DashPage.Arrange(new Rect(0, 0, w, h - 86));
                window.DashPage.UpdateLayout();

                Assert.IsType<PathGeometry>(window.WindowRoot.Clip);
                Assert.True(window.DashPage.CpuGraphGrid.ActualWidth > 0, $"CPU graph collapsed at {w}x{h} @{scale}x");
                Assert.True(window.DashPage.GpuGraphGrid.ActualWidth > 0, $"GPU graph collapsed at {w}x{h} @{scale}x");

                var bitmap = RenderVisual(window.WindowRoot, w, h, scale);
                Assert.Equal((int)Math.Ceiling(w * scale), bitmap.PixelWidth);
                Assert.Equal((int)Math.Ceiling(h * scale), bitmap.PixelHeight);

                var stats = AnalyzeWpf(bitmap);
                Assert.True(stats.OpaqueFraction > 0.85, $"Dashboard mostly transparent at {w}x{h} @{scale}x: {stats.OpaqueFraction:F2}");
                Assert.True(stats.DistinctColors > 20, $"Dashboard near-blank at {w}x{h} @{scale}x: {stats.DistinctColors} colors");
                Assert.True(stats.CutCornerAlpha < 16, $"Chamfer corner not cut at {w}x{h} @{scale}x: A={stats.CutCornerAlpha}");
                Assert.True(stats.SharpCornerAlpha > 200, $"Sharp corner not rendered at {w}x{h} @{scale}x: A={stats.SharpCornerAlpha}");
                Assert.True(stats.CenterAlpha > 200, $"Dashboard center transparent at {w}x{h} @{scale}x: A={stats.CenterAlpha}");
            }
            finally { window.Close(); }
        });
    }

    // ------------------------------------------------------------- Fan Curve

    [Theory]
    [InlineData(880, 660, 1.0)]
    [InlineData(880, 660, 1.5)]
    [InlineData(880, 660, 2.0)]
    [InlineData(700, 540, 1.5)]   // shrunk below design size
    [InlineData(1100, 800, 1.25)] // enlarged
    public void FanCurve_RendersCurveInsideCanvas_AcrossSizesAndDpi(double w, double h, double scale)
    {
        RunInSta(() =>
        {
            using var vm = new DashboardViewModel();
            var window = new FanCurveWindow(vm);
            try
            {
                window.Show();
                window.Width = w;
                window.Height = h;
                window.UpdateLayout();
                window.UpdateChamferGeometry();
                // See Dashboard case: force layout manually on the test thread.
                // (Window.Measure itself FailFasts in the test host; arrange content.)
                window.WindowRoot.Measure(new WpfSize(w, h));
                window.WindowRoot.Arrange(new Rect(0, 0, w, h));
                window.WindowRoot.UpdateLayout();

                Assert.IsType<PathGeometry>(window.WindowRoot.Clip);
                Assert.True(window.GraphCanvas.ActualWidth > 100, $"Graph canvas collapsed at {w}x{h} @{scale}x");
                Assert.True(window.GraphCanvas.ActualHeight > 100, $"Graph canvas collapsed at {w}x{h} @{scale}x");
                Assert.True(window.CurveLine.Points.Count >= 5, $"Curve not drawn at {w}x{h} @{scale}x");
                foreach (var p in window.CurveLine.Points)
                {
                    Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y), $"NaN curve point at {w}x{h} @{scale}x");
                    Assert.InRange(p.X, -0.6, window.GraphCanvas.ActualWidth + 0.6);
                    Assert.InRange(p.Y, -0.6, window.GraphCanvas.ActualHeight + 0.6);
                }

                var bitmap = RenderVisual(window.WindowRoot, w, h, scale);
                var stats = AnalyzeWpf(bitmap);
                Assert.True(stats.OpaqueFraction > 0.85, $"FanCurve mostly transparent at {w}x{h} @{scale}x: {stats.OpaqueFraction:F2}");
                Assert.True(stats.DistinctColors > 20, $"FanCurve near-blank at {w}x{h} @{scale}x: {stats.DistinctColors} colors");
                Assert.True(stats.CutCornerAlpha < 16, $"Chamfer corner not cut at {w}x{h} @{scale}x: A={stats.CutCornerAlpha}");
                Assert.True(stats.SharpCornerAlpha > 200, $"Sharp corner not rendered at {w}x{h} @{scale}x: A={stats.SharpCornerAlpha}");

                // Ember curve stroke / point handles visible inside the graph region
                var region = window.GraphCanvas.TransformToAncestor(window.WindowRoot)
                    .TransformBounds(new Rect(0, 0, window.GraphCanvas.ActualWidth, window.GraphCanvas.ActualHeight));
                int ember = CountEmberPixels(bitmap, region, scale);
                Assert.True(ember >= 10, $"Curve stroke missing in graph at {w}x{h} @{scale}x: {ember}px");
            }
            finally { window.Close(); }
        });
    }

    // --------------------------------------------------------- pixel helpers

    private static RenderTargetBitmap RenderVisual(FrameworkElement visual, double w, double h, double scale)
    {
        int pxW = Math.Max(1, (int)Math.Ceiling(w * scale));
        int pxH = Math.Max(1, (int)Math.Ceiling(h * scale));
        var bitmap = new RenderTargetBitmap(pxW, pxH, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private sealed record WpfStats(double OpaqueFraction, int DistinctColors, int CutCornerAlpha, int SharpCornerAlpha, int CenterAlpha);

    private static WpfStats AnalyzeWpf(RenderTargetBitmap bitmap)
    {
        int stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        long opaque = 0, total = 0;
        var distinct = new HashSet<int>();
        for (int i = 0; i + 3 < pixels.Length; i += 4 * 7)
        {
            total++;
            if (pixels[i + 3] > 200) opaque++;
            distinct.Add(BitConverter.ToInt32(pixels, i));
        }

        // Window chamfer cuts top-right / bottom-left; top-left stays sharp.
        int cutCorner = pixels[(bitmap.PixelWidth - 1) * 4 + 3];
        int sharpCorner = pixels[3];
        int centerIdx = ((bitmap.PixelHeight / 2) * stride) + (bitmap.PixelWidth / 2) * 4;
        return new WpfStats((double)opaque / Math.Max(1, total), distinct.Count, cutCorner, sharpCorner, pixels[centerIdx + 3]);
    }

    private static int CountEmberPixels(RenderTargetBitmap bitmap, Rect logicalRegion, double scale)
    {
        int stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        int x0 = Math.Clamp((int)(logicalRegion.X * scale), 0, bitmap.PixelWidth - 1);
        int x1 = Math.Clamp((int)(logicalRegion.Right * scale), x0 + 1, bitmap.PixelWidth);
        int y0 = Math.Clamp((int)(logicalRegion.Y * scale), 0, bitmap.PixelHeight - 1);
        int y1 = Math.Clamp((int)(logicalRegion.Bottom * scale), y0 + 1, bitmap.PixelHeight);

        int count = 0;
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            int i = y * stride + x * 4;
            byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
            if (a > 200 && r > 170 && g is >= 40 and <= 150 && b < 100) count++;
        }
        return count;
    }

    private sealed record GdiStats(double OpaqueFraction, int DistinctColors);

    private static GdiStats AnalyzeGdi(Bitmap bitmap)
    {
        var rect = new DrawingRect(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, GdiPixelFormat.Format32bppPArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            long opaque = 0, total = 0;
            var distinct = new HashSet<int>();
            for (int i = 0; i + 3 < pixels.Length; i += 4 * 7)
            {
                total++;
                if (pixels[i + 3] > 200) opaque++;
                distinct.Add(BitConverter.ToInt32(pixels, i));
            }
            return new GdiStats((double)opaque / Math.Max(1, total), distinct.Count);
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static int CountNearColor(Bitmap bitmap, DrawingRect region, Color target, int tolerance)
    {
        region.Intersect(new DrawingRect(0, 0, bitmap.Width, bitmap.Height));
        if (region.Width <= 0 || region.Height <= 0) return 0;

        var data = bitmap.LockBits(region, ImageLockMode.ReadOnly, GdiPixelFormat.Format32bppPArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * region.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            int count = 0;
            for (int i = 0; i + 3 < pixels.Length; i += 4)
            {
                if (pixels[i + 3] < 200) continue;
                int dist = Math.Abs(pixels[i + 2] - target.R) + Math.Abs(pixels[i + 1] - target.G) + Math.Abs(pixels[i] - target.B);
                if (dist <= tolerance) count++;
            }
            return count;
        }
        finally { bitmap.UnlockBits(data); }
    }
}
