﻿using System;
using System.Diagnostics.CodeAnalysis;

using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;

using Nefarius.Utilities.DeviceManagement.Exceptions;

namespace Nefarius.Utilities.DeviceManagement.PnP;

/// <summary>
///     A variable of ULONG type that supplies one of the following flag values that apply if the caller supplies a device
///     instance identifier:
///     CM_LOCATE_DEVNODE_NORMAL
///     The function retrieves the device instance handle for the specified device only if the device is currently
///     configured in the device tree.
///     CM_LOCATE_DEVNODE_PHANTOM
///     The function retrieves a device instance handle for the specified device if the device is currently configured in
///     the device tree or the device is a nonpresent device that is not currently configured in the device tree.
///     CM_LOCATE_DEVNODE_CANCELREMOVE
///     The function retrieves a device instance handle for the specified device if the device is currently configured in
///     the device tree or in the process of being removed from the device tree. If the device is in the process of being
///     removed, the function cancels the removal of the device.
/// </summary>
public enum DeviceLocationFlags
{
    /// <summary>
    ///     The function retrieves the device instance handle for the specified device only if the device is currently
    ///     configured in the device tree.
    /// </summary>
    Normal,

    /// <summary>
    ///     The function retrieves a device instance handle for the specified device if the device is currently configured in
    ///     the device tree or the device is a nonpresent device that is not currently configured in the device tree.
    /// </summary>
    Phantom,

    /// <summary>
    ///     The function retrieves a device instance handle for the specified device if the device is currently configured in
    ///     the device tree or in the process of being removed from the device tree. If the device is in the process of being
    ///     removed, the function cancels the removal of the device.
    /// </summary>
    CancelRemove
}

/// <summary>
///     Describes an instance of a PNP device.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public interface IPnPDevice
{
    /// <summary>
    ///     The instance ID of the device.
    /// </summary>
    string InstanceId { get; }

    /// <summary>
    ///     The device ID.
    /// </summary>
    string DeviceId { get; }

    /// <summary>
    ///     Attempts to restart this device. Device restart may fail if it has open handles that currently can not be
    ///     force-closed.
    /// </summary>
    void Restart();

    /// <summary>
    ///     Attempts to remove this device node.
    /// </summary>
    void Remove();

    /// <summary>
    ///     Walks up the <see cref="PnPDevice" />s parents chain to determine if the top most device is root enumerated.
    /// </summary>
    /// <remarks>
    ///     This is achieved by walking up the node tree until the top most parent and check if the last parent below the
    ///     tree root is a software device. Hardware devices originate from a PCI(e) bus while virtual devices originate from a
    ///     root enumerated device.
    /// </remarks>
    /// <param name="excludeIfMatches">Returns false if the given predicate is true.</param>
    /// <returns>True if this devices originates from an emulator, false otherwise.</returns>
    bool IsVirtual(Func<IPnPDevice, bool> excludeIfMatches = default);

    /// <summary>
    ///     Returns a device instance property identified by <see cref="DevicePropertyKey" />.
    /// </summary>
    /// <typeparam name="T">The managed type of the fetched property value.</typeparam>
    /// <param name="propertyKey">The <see cref="DevicePropertyKey" /> to query for.</param>
    /// <returns>On success, the value of the queried property.</returns>
    T GetProperty<T>(DevicePropertyKey propertyKey);

    /// <summary>
    ///     Creates or updates an existing property with a given value.
    /// </summary>
    /// <typeparam name="T">The type of the property.</typeparam>
    /// <param name="propertyKey">The <see cref="DevicePropertyKey" /> to update.</param>
    /// <param name="propertyValue">The value to set.</param>
    void SetProperty<T>(DevicePropertyKey propertyKey, T propertyValue);
}

/// <summary>
///     Describes an instance of a PNP device.
/// </summary>
public partial class PnPDevice : IPnPDevice, IEquatable<PnPDevice>
{
    private readonly uint _instanceHandle;

    /// <summary>
    ///     Creates a new <see cref="PnPDevice" /> based on the supplied instance ID to search in the device tree.
    /// </summary>
    /// <param name="instanceId">The instance ID to look for.</param>
    /// <param name="flags">The <see cref="DeviceLocationFlags" /> influencing search behavior.</param>
    /// <exception cref="ConfigManagerException"></exception>
    protected unsafe PnPDevice(string instanceId, DeviceLocationFlags flags)
    {
        InstanceId = instanceId;
        uint iFlags = PInvoke.CM_LOCATE_DEVNODE_NORMAL;

        switch (flags)
        {
            case DeviceLocationFlags.Normal:
                iFlags = PInvoke.CM_LOCATE_DEVNODE_NORMAL;
                break;
            case DeviceLocationFlags.Phantom:
                iFlags = PInvoke.CM_LOCATE_DEVNODE_PHANTOM;
                break;
            case DeviceLocationFlags.CancelRemove:
                iFlags = PInvoke.CM_LOCATE_DEVNODE_CANCELREMOVE;
                break;
        }

        fixed (char* pInstId = instanceId)
        {
            CONFIGRET ret = PInvoke.CM_Locate_DevNode(
                out _instanceHandle,
                pInstId,
                iFlags
            );

            if (ret == CONFIGRET.CR_NO_SUCH_DEVINST)
            {
                throw new ArgumentException("The supplied instance wasn't found.", nameof(instanceId));
            }

            if (ret != CONFIGRET.CR_SUCCESS)
            {
                throw new ConfigManagerException("Failed to locate device node.", ret);
            }

            ret = PInvoke.CM_Get_Device_ID_Size(out uint charsRequired, _instanceHandle, 0);

            if (ret != CONFIGRET.CR_SUCCESS)
            {
                throw new ConfigManagerException("Fetching device ID size failed.", ret);
            }

            uint nBytes = (charsRequired + 1) * 2;
            char* ptrInstanceBuf = stackalloc char[(int)nBytes];

            ret = PInvoke.CM_Get_Device_IDW(
                _instanceHandle,
                ptrInstanceBuf,
                nBytes,
                0
            );

            if (ret != CONFIGRET.CR_SUCCESS)
            {
                throw new ConfigManagerException("Fetching device ID failed.", ret);
            }

            DeviceId = new string(ptrInstanceBuf);
        }
    }

    /// <summary>
    ///     The instance ID of the device. Uniquely identifies devices of equal make and model on the same machine.
    /// </summary>
    public string InstanceId { get; }

    /// <summary>
    ///     The device ID. Typically built from the hardware ID of the same make and model of hardware.
    /// </summary>
    public string DeviceId { get; }

    /// <summary>
    ///     Attempts to restart this device. Device restart may fail if it has open handles that currently can not be
    ///     force-closed.
    /// </summary>
    /// <exception cref="ConfigManagerException"></exception>
    public unsafe void Restart()
    {
        CONFIGRET ret = PInvoke.CM_Query_And_Remove_SubTree(
            _instanceHandle,
            null, null, 0,
            PInvoke.CM_REMOVE_NO_RESTART
        );

        if (ret != CONFIGRET.CR_SUCCESS)
        {
            throw new ConfigManagerException("Node removal failed.", ret);
        }

        ret = PInvoke.CM_Setup_DevNode(
            _instanceHandle,
            PInvoke.CM_SETUP_DEVNODE_READY
        );

        if (ret is CONFIGRET.CR_NO_SUCH_DEVINST or CONFIGRET.CR_SUCCESS)
        {
            return;
        }

        throw new ConfigManagerException("Node addition failed.", ret);
    }

    /// <summary>
    ///     Attempts to remove this device node.
    /// </summary>
    /// <exception cref="ConfigManagerException"></exception>
    public unsafe void Remove()
    {
        CONFIGRET ret = PInvoke.CM_Query_And_Remove_SubTree(
            _instanceHandle,
            null, null, 0,
            PInvoke.CM_REMOVE_NO_RESTART
        );

        if (ret != CONFIGRET.CR_SUCCESS)
        {
            throw new ConfigManagerException("Node removal failed.", ret);
        }
    }

    /// <summary>
    ///     Walks up the <see cref="PnPDevice" />s parents chain to determine if the top most device is root enumerated.
    /// </summary>
    /// <remarks>
    ///     This is achieved by walking up the node tree until the top most parent and check if the last parent below the
    ///     tree root is a software device. Hardware devices originate from a PCI(e) bus while virtual devices originate from a
    ///     root enumerated device.
    /// </remarks>
    /// <param name="excludeIfMatches">Returns false if the given predicate is true.</param>
    /// <returns>True if this devices originates from an emulator, false otherwise.</returns>
    public bool IsVirtual(Func<IPnPDevice, bool> excludeIfMatches = default)
    {
        IPnPDevice device = this;

        while (device is not null)
        {
            if (excludeIfMatches != null && excludeIfMatches(device))
            {
                return false;
            }

            string parentId = device.GetProperty<string>(DevicePropertyKey.Device_Parent);

            if (parentId.Equals(@"HTREE\ROOT\0", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            device = GetDeviceByInstanceId(parentId, DeviceLocationFlags.Phantom);
        }

        //
        // TODO: test how others behave (reWASD, NVIDIA, ...)
        // 
        return device is not null &&
               (device.InstanceId.StartsWith(@"ROOT\SYSTEM", StringComparison.OrdinalIgnoreCase)
                || device.InstanceId.StartsWith(@"ROOT\USB", StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return InstanceId;
    }

    #region IEquatable

    /// <inheritdoc />
    public bool Equals(PnPDevice other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return string.Equals(DeviceId, other.DeviceId, StringComparison.InvariantCultureIgnoreCase);
    }

    /// <inheritdoc />
    public override bool Equals(object obj)
    {
        return ReferenceEquals(this, obj) || (obj is PnPDevice other && Equals(other));
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return StringComparer.InvariantCultureIgnoreCase.GetHashCode(DeviceId);
    }

    /// <summary>
    ///     Compares two instances of <see cref="PnPDevice" /> for equality.
    /// </summary>
    public static bool operator ==(PnPDevice left, PnPDevice right)
    {
        return Equals(left, right);
    }

    /// <summary>
    ///     Compares two instances of <see cref="PnPDevice" /> for equality.
    /// </summary>
    public static bool operator !=(PnPDevice left, PnPDevice right)
    {
        return !Equals(left, right);
    }

    #endregion
}