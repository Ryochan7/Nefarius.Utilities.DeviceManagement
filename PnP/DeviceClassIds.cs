﻿using System;
using System.Diagnostics.CodeAnalysis;

namespace Nefarius.Utilities.DeviceManagement.PnP;

/// <summary>
///     Provides common device class <see cref="Guid" />s.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static class DeviceClassIds
{
    /// <summary>
    ///     USB devices.
    /// </summary>
    public static Guid Usb => Guid.Parse("{36FC9E60-C465-11CF-8056-444553540000}");

    /// <summary>
    ///     Xbox 360 Peripherals.
    /// </summary>
    public static Guid XnaComposite => Guid.Parse("{d61ca365-5af4-4486-998b-9db4734c6ca3}");

    /// <summary>
    ///     Xbox Peripherals.
    /// </summary>
    public static Guid XboxComposite => Guid.Parse("{05f5cfe2-4733-4950-a6bb-07aad01a3a84}");
}
