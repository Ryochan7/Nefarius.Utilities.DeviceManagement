﻿using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Nefarius.Utilities.DeviceManagement.PnP
{
    /// <summary>
    /// A variable of ULONG type that supplies one of the following flag values that apply if the caller supplies a device instance identifier:
    ///
    ///    CM_LOCATE_DEVNODE_NORMAL
    ///        The function retrieves the device instance handle for the specified device only if the device is currently configured in the device tree.
    ///
    ///    CM_LOCATE_DEVNODE_PHANTOM
    ///        The function retrieves a device instance handle for the specified device if the device is currently configured in the device tree or the device is a nonpresent device that is not currently configured in the device tree.
    ///
    ///    CM_LOCATE_DEVNODE_CANCELREMOVE
    ///        The function retrieves a device instance handle for the specified device if the device is currently configured in the device tree or in the process of being removed from the device tree. If the device is in the process of being removed, the function cancels the removal of the device.
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
        ///     Attempts to restart this device. Device restart may fail if it has open handles that currently can not be force-closed.
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
        /// <returns>True if this devices originates from an emulator, false otherwise.</returns>
        bool IsVirtual();

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
        /// <param name="propertyKey">The <see cref="DevicePropertyKey"/> to update.</param>
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
        ///     Creates a new <see cref="PnPDevice"/> based on the supplied instance ID to search in the device tree.
        /// </summary>
        /// <param name="instanceId">The instance ID to look for.</param>
        /// <param name="flags">The <see cref="DeviceLocationFlags"/> influencing search behavior.</param>
        protected PnPDevice(string instanceId, DeviceLocationFlags flags)
        {
            InstanceId = instanceId;
            var iFlags = SetupApiWrapper.CM_LOCATE_DEVNODE_FLAG.CM_LOCATE_DEVNODE_NORMAL;

            switch (flags)
            {
                case DeviceLocationFlags.Normal:
                    iFlags = SetupApiWrapper.CM_LOCATE_DEVNODE_FLAG.CM_LOCATE_DEVNODE_NORMAL;
                    break;
                case DeviceLocationFlags.Phantom:
                    iFlags = SetupApiWrapper.CM_LOCATE_DEVNODE_FLAG.CM_LOCATE_DEVNODE_PHANTOM;
                    break;
                case DeviceLocationFlags.CancelRemove:
                    iFlags = SetupApiWrapper.CM_LOCATE_DEVNODE_FLAG.CM_LOCATE_DEVNODE_CANCELREMOVE;
                    break;
            }

            var ret = SetupApiWrapper.CM_Locate_DevNode(
                ref _instanceHandle,
                instanceId,
                iFlags
            );

            if (ret == SetupApiWrapper.ConfigManagerResult.NoSuchDevinst)
                throw new ArgumentException("The supplied instance wasn't found.", nameof(instanceId));

            if (ret != SetupApiWrapper.ConfigManagerResult.Success)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            uint nBytes = 256;

            var ptrInstanceBuf = Marshal.AllocHGlobal((int)nBytes);

            try
            {
                ret = SetupApiWrapper.CM_Get_Device_ID(
                    _instanceHandle,
                    ptrInstanceBuf,
                    nBytes,
                    0
                );

                if (ret != SetupApiWrapper.ConfigManagerResult.Success)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                DeviceId = (Marshal.PtrToStringUni(ptrInstanceBuf) ?? string.Empty).ToUpper();
            }
            finally
            {
                if (ptrInstanceBuf != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptrInstanceBuf);
            }
        }

        /// <inheritdoc />
        public string InstanceId { get; }

        /// <inheritdoc />
        public string DeviceId { get; }

        /// <inheritdoc />
        public void Restart()
        {
            var ret = SetupApiWrapper.CM_Query_And_Remove_SubTree(
                _instanceHandle,
                IntPtr.Zero, IntPtr.Zero, 0,
                SetupApiWrapper.CM_QUERY_AND_REMOVE_SUBTREE_FLAGS.CM_REMOVE_NO_RESTART
            );

            if (ret != SetupApiWrapper.ConfigManagerResult.Success)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            ret = SetupApiWrapper.CM_Setup_DevNode(
                _instanceHandle,
                SetupApiWrapper.CM_SETUP_DEVINST_FLAGS.CM_SETUP_DEVNODE_READY
            );

            if (ret != SetupApiWrapper.ConfigManagerResult.NoSuchDevinst
                && ret != SetupApiWrapper.ConfigManagerResult.Success)
                if (ret != SetupApiWrapper.ConfigManagerResult.Success)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        /// <inheritdoc />
        public void Remove()
        {
            var ret = SetupApiWrapper.CM_Query_And_Remove_SubTree(
                _instanceHandle,
                IntPtr.Zero, IntPtr.Zero, 0,
                SetupApiWrapper.CM_QUERY_AND_REMOVE_SUBTREE_FLAGS.CM_REMOVE_NO_RESTART
            );

            if (ret != SetupApiWrapper.ConfigManagerResult.Success)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        /// <inheritdoc />
        public bool IsVirtual()
        {
            IPnPDevice device = this;

            while (device is not null)
            {
                var parentId = device.GetProperty<string>(DevicePropertyDevice.Parent);

                if (parentId.Equals(@"HTREE\ROOT\0", StringComparison.OrdinalIgnoreCase))
                    break;

                device = GetDeviceByInstanceId(parentId);
            }

            //
            // TODO: test how others behave (reWASD, NVIDIA, ...)
            // 
            return device is not null &&
                   (device.InstanceId.StartsWith(@"ROOT\SYSTEM", StringComparison.OrdinalIgnoreCase)
                    || device.InstanceId.StartsWith(@"ROOT\USB", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        ///     Return device identified by instance ID.
        /// </summary>
        /// <param name="instanceId">The instance ID of the device.</param>
        /// <param name="flags">
        ///     <see cref="DeviceLocationFlags" />
        /// </param>
        /// <returns>A <see cref="PnPDevice" />.</returns>
        public static PnPDevice GetDeviceByInstanceId(string instanceId, DeviceLocationFlags flags = DeviceLocationFlags.Normal)
        {
            return new PnPDevice(instanceId, flags);
        }

        /// <summary>
        ///     Return device identified by instance ID/path (symbolic link).
        /// </summary>
        /// <param name="symbolicLink">The device interface path/ID/symbolic link name.</param>
        /// <param name="flags">
        ///     <see cref="DeviceLocationFlags" />
        /// </param>
        /// <returns>A <see cref="PnPDevice" />.</returns>
        public static PnPDevice GetDeviceByInterfaceId(string symbolicLink, DeviceLocationFlags flags = DeviceLocationFlags.Normal)
        {
            var instanceId = GetInstanceIdFromInterfaceId(symbolicLink);

            return GetDeviceByInstanceId(instanceId, flags);
        }

        /// <summary>
        ///     Resolves Interface ID/Symbolic link/Device path to Instance ID.
        /// </summary>
        /// <param name="symbolicLink">The device interface path/ID/symbolic link name.</param>
        /// <returns>The Instance ID.</returns>
        public static string GetInstanceIdFromInterfaceId(string symbolicLink)
        {
            var property = DevicePropertyDevice.InstanceId.ToNativeType();

            var buffer = IntPtr.Zero;
            uint sizeRequired = 2048;

            try
            {
                buffer = Marshal.AllocHGlobal((int)sizeRequired);

                var ret = SetupApiWrapper.CM_Get_Device_Interface_Property(
                    symbolicLink,
                    ref property,
                    out _,
                    buffer,
                    ref sizeRequired,
                    0
                );

                if (ret != SetupApiWrapper.ConfigManagerResult.Success)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
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
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(DeviceId, other.DeviceId, StringComparison.InvariantCultureIgnoreCase);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return ReferenceEquals(this, obj) || obj is PnPDevice other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return StringComparer.InvariantCultureIgnoreCase.GetHashCode(DeviceId);
        }

        /// <summary>
        ///     Compares two instances of <see cref="PnPDevice"/> for equality.
        /// </summary>
        public static bool operator ==(PnPDevice left, PnPDevice right)
        {
            return Equals(left, right);
        }

        /// <summary>
        ///     Compares two instances of <see cref="PnPDevice"/> for equality.
        /// </summary>
        public static bool operator !=(PnPDevice left, PnPDevice right)
        {
            return !Equals(left, right);
        }

        #endregion
    }
}