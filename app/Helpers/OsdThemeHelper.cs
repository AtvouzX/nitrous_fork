using System.Drawing;
using Nitrous.Enums;

namespace Nitrous.Helpers;

/// <summary>
/// Accent and surface colors for the forged-metal OSD overlay.
/// Values mirror the reference mock (Quiet steel-blue, Balanced green,
/// Performance ember-orange, Turbo violet) so tray and OSD agree.
/// </summary>
public static class OsdThemeHelper
{
    public static Color Surface => Color.FromArgb(20, 20, 22);
    public static Color OuterBorder => Color.FromArgb(0x44, 0x44, 0x44);
    public static Color InnerBorder => Color.FromArgb(0x22, 0x22, 0x22);
    public static Color SubtitleText => Color.FromArgb(0xB3, 0xB3, 0xB3);
    public static Color TitleText => Color.White;

    public static Color GetAccent(PowerProfile profile) => profile switch
    {
        PowerProfile.Quiet => Color.FromArgb(0x4A, 0xA3, 0xBA),
        PowerProfile.Balanced => Color.FromArgb(0x5C, 0xAF, 0x6F),
        PowerProfile.Performance => Color.FromArgb(0xE6, 0x75, 0x36),
        PowerProfile.Turbo => Color.FromArgb(0xA8, 0x5D, 0xB0),
        _ => Color.White
    };
}
