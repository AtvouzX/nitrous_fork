using System;

namespace Nitrous.Enums;

public enum PowerProfile : ulong
{
    Quiet = 0x00,
    Balanced = 0x01,
    Performance = 0x04,
    Turbo = 0x05,
    Eco = 0x06
}
