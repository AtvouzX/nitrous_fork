using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Nitrous.Helpers;
using Nitrous.Ui.Controls;
using Xunit;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;

namespace Nitrous.Tests;

public class ChamferButtonTests
{
    private static void RunInSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    _ = new Application();
                }
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (ex != null)
        {
            throw new AggregateException("STA thread encountered an uncaught exception.", ex);
        }
    }

    [Fact]
    public void ChamferButton_DefaultValues_AreCorrect()
    {
        RunInSta(() =>
        {
            var btn = new ChamferButton();
            Assert.Equal(ChamferGeometryHelper.DefaultButtonChamfer, btn.ChamferSize);
            Assert.Equal(ChamferButtonVariant.Ghost, btn.Variant);
            Assert.NotNull(btn.Template);
        });
    }

    [Fact]
    public void ChamferButton_Variant_CanBeChangedToAccent()
    {
        RunInSta(() =>
        {
            var btn = new ChamferButton
            {
                Variant = ChamferButtonVariant.Primary
            };
            Assert.Equal(ChamferButtonVariant.Primary, btn.Variant);
            Assert.Equal(ChamferButtonVariant.Accent, btn.Variant);

            btn.Variant = ChamferButtonVariant.Secondary;
            Assert.Equal(ChamferButtonVariant.Secondary, btn.Variant);
            Assert.Equal(ChamferButtonVariant.Ghost, btn.Variant);
        });
    }

    [Fact]
    public void ChamferButton_UpdateButtonGeometry_GeneratesValidClips()
    {
        RunInSta(() =>
        {
            var btn = new ChamferButton
            {
                Width = 100,
                Height = 32,
                ChamferSize = 4.0,
                Variant = ChamferButtonVariant.Accent
            };

            btn.ApplyTemplate();
            btn.UpdateButtonGeometry();

            Assert.NotNull(btn.Clip);
            var clipGeom = Assert.IsType<PathGeometry>(btn.Clip);
            Assert.Single(clipGeom.Figures);
            Assert.True(clipGeom.Figures[0].IsClosed);
            Assert.Equal(5, clipGeom.Figures[0].Segments.Count);
        });
    }

    [Fact]
    public void ChamferButton_HandlesZeroOrNegativeDimensionsGracefully()
    {
        RunInSta(() =>
        {
            var btn = new ChamferButton
            {
                Width = 0,
                Height = 0
            };

            btn.ApplyTemplate();
            // Should not throw or crash
            btn.UpdateButtonGeometry();
        });
    }

    [Fact]
    public void ChamferButton_UpdateVisualBrushes_AppliesCorrectStylesPerState()
    {
        RunInSta(() =>
        {
            var btn = new ChamferButton
            {
                Width = 80,
                Height = 28,
                Variant = ChamferButtonVariant.Ghost
            };

            btn.ApplyTemplate();
            btn.UpdateButtonGeometry();
            btn.UpdateVisualBrushes();

            Assert.Equal(1.0, btn.Opacity);

            // Test disabled state
            btn.IsEnabled = false;
            btn.UpdateVisualBrushes();
            Assert.Equal(0.35, btn.Opacity);
        });
    }
}

