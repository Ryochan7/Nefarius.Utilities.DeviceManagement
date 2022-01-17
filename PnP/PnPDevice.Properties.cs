﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace Nefarius.Utilities.DeviceManagement.PnP
{
    public partial class PnPDevice
    {
        private static readonly IDictionary<SetupApiWrapper.DevPropType, Type> NativeToManagedTypeMap =
            new Dictionary<SetupApiWrapper.DevPropType, Type>
            {
                {SetupApiWrapper.DevPropType.Sbyte, typeof(sbyte)},
                {SetupApiWrapper.DevPropType.Byte, typeof(byte)},
                {SetupApiWrapper.DevPropType.Int16, typeof(short)},
                {SetupApiWrapper.DevPropType.Uint16, typeof(ushort)},
                {SetupApiWrapper.DevPropType.Int32, typeof(int)},
                {SetupApiWrapper.DevPropType.Uint32, typeof(uint)},
                {SetupApiWrapper.DevPropType.Int64, typeof(long)},
                {SetupApiWrapper.DevPropType.Uint64, typeof(ulong)},
                {SetupApiWrapper.DevPropType.Float, typeof(float)},
                {SetupApiWrapper.DevPropType.Double, typeof(double)},
                {SetupApiWrapper.DevPropType.Decimal, typeof(decimal)},
                {SetupApiWrapper.DevPropType.Guid, typeof(Guid)},
                // DEVPROP_TYPE_CURRENCY
                {SetupApiWrapper.DevPropType.Date, typeof(DateTime)},
                {SetupApiWrapper.DevPropType.FileTime, typeof(DateTimeOffset)},
                {SetupApiWrapper.DevPropType.Boolean, typeof(bool)},
                {SetupApiWrapper.DevPropType.String, typeof(string)},
                {SetupApiWrapper.DevPropType.StringList, typeof(string[])},
                // DEVPROP_TYPE_SECURITY_DESCRIPTOR
                // DEVPROP_TYPE_SECURITY_DESCRIPTOR_STRING
                {SetupApiWrapper.DevPropType.Devpropkey, typeof(SetupApiWrapper.DevPropKey)},
                {SetupApiWrapper.DevPropType.Devproptype, typeof(SetupApiWrapper.DevPropType)},
                {SetupApiWrapper.DevPropType.Binary, typeof(byte[])},
                {SetupApiWrapper.DevPropType.Error, typeof(int)},
                {SetupApiWrapper.DevPropType.Ntstatus, typeof(int)}
                // DEVPROP_TYPE_STRING_INDIRECT
            };

        /// <summary>
        ///     Returns a device instance property identified by <see cref="DevicePropertyKey" />.
        /// </summary>
        /// <typeparam name="T">The managed type of the fetched porperty value.</typeparam>
        /// <param name="propertyKey">The <see cref="DevicePropertyKey" /> to query for.</param>
        /// <returns>On success, the value of the queried property.</returns>
        public T GetProperty<T>(DevicePropertyKey propertyKey)
        {
            if (typeof(T) != propertyKey.PropertyType)
                throw new ArgumentException(
                    "The supplied object type doesn't match the property type.",
                    nameof(propertyKey)
                );

            var buffer = IntPtr.Zero;

            try
            {
                var ret = GetProperty(
                    propertyKey.ToNativeType(),
                    out var propertyType,
                    out buffer,
                    out var size
                );

                if (ret == SetupApiWrapper.ConfigManagerResult.NoSuchValue
                    || propertyType == SetupApiWrapper.DevPropType.Empty)
                    return default(T);

                if (ret != SetupApiWrapper.ConfigManagerResult.Success)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                if (!NativeToManagedTypeMap.TryGetValue(propertyType, out var managedType))
                    throw new ArgumentException(
                        "Unknown property type.",
                        nameof(propertyKey)
                    );

                if (typeof(T) != managedType)
                    throw new ArgumentException(
                        "The supplied object type doesn't match the property type.",
                        nameof(propertyKey)
                    );

                #region Don't look, nasty trickery

                /*
                 * Handle some native to managed conversions
                 */

                // Regular strings
                if (managedType == typeof(string))
                {
                    var value = Marshal.PtrToStringUni(buffer);
                    return (T)Convert.ChangeType(value, typeof(T));
                }

                // Double-null-terminated string to list
                if (managedType == typeof(string[]))
                    return (T)(object)Marshal.PtrToStringUni(buffer, (int)size / 2).TrimEnd('\0').Split('\0')
                        .ToArray();

                // Byte & SByte
                if (managedType == typeof(sbyte)
                    || managedType == typeof(byte))
                    return (T)(object)Marshal.ReadByte(buffer);

                // (U)Int16
                if (managedType == typeof(short)
                    || managedType == typeof(ushort))
                    return (T)(object)(ushort)Marshal.ReadInt16(buffer);

                // (U)Int32
                if (managedType == typeof(int)
                    || managedType == typeof(uint))
                    return (T)Convert.ChangeType(Marshal.ReadInt32(buffer), managedType);

                // (U)Int64
                if (managedType == typeof(long)
                    || managedType == typeof(ulong))
                    return (T)(object)(ulong)Marshal.ReadInt64(buffer);

                // FILETIME/DateTimeOffset
                if (managedType == typeof(DateTimeOffset))
                    return (T)(object)DateTimeOffset.FromFileTime(Marshal.ReadInt64(buffer));

                // GUID
                if (managedType == typeof(Guid))
                    return (T)(object)(Guid)Marshal.PtrToStructure<Guid>(buffer);

                #endregion

                throw new NotImplementedException("Type not supported.");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        ///     Creates or updates an existing property with a given value.
        /// </summary>
        /// <typeparam name="T">The type of the property.</typeparam>
        /// <param name="propertyKey">The <see cref="DevicePropertyKey"/> to update.</param>
        /// <param name="propertyValue">The value to set.</param>
        public void SetProperty<T>(DevicePropertyKey propertyKey, T propertyValue)
        {
            if (typeof(T) != propertyKey.PropertyType)
                throw new ArgumentException(
                    "The supplied object type doesn't match the property type.",
                    nameof(propertyKey)
                );

            var managedType = typeof(T);

            var nativePropKey = propertyKey.ToNativeType();

            var nativePropType = NativeToManagedTypeMap.FirstOrDefault(t => t.Value == managedType).Key;

            uint propBufSize = 0;

            IntPtr buffer = IntPtr.Zero;

            #region Don't look, nasty trickery

            /*
             * Handle some native to managed conversions
             */

            // Regular strings
            if (managedType == typeof(string))
            {
                var value = (string)(object)propertyValue;
                buffer = Marshal.StringToHGlobalUni(value);
                propBufSize = (uint)((value.Length + 1) * 2);
            }

            // Double-null-terminated string to list
            //if (managedType == typeof(string[]))
            //    return (T) (object) Marshal.PtrToStringUni(buffer, (int) size / 2).TrimEnd('\0').Split('\0')
            //        .ToArray();

            // Byte & SByte
            if (managedType == typeof(sbyte)
                || managedType == typeof(byte))
            {
                var value = (byte)(object)propertyValue;
                propBufSize = (uint)Marshal.SizeOf(managedType);
                buffer = Marshal.AllocHGlobal((int)propBufSize);
                Marshal.WriteByte(buffer, value);
            }
            /*
            // (U)Int16
            if (managedType == typeof(short)
                || managedType == typeof(ushort))
                return (T) (object) (ushort) Marshal.ReadInt16(buffer);
            */
            // (U)Int32
            if (managedType == typeof(int)
                || managedType == typeof(uint))
            {
                var value = (uint)(object)propertyValue;
                propBufSize = (uint)Marshal.SizeOf(managedType);
                buffer = Marshal.AllocHGlobal((int)propBufSize);
                Marshal.WriteInt32(buffer, (int)value);
            }
            /*
            // (U)Int64
            if (managedType == typeof(long)
                || managedType == typeof(ulong))
                return (T) (object) (ulong) Marshal.ReadInt64(buffer);

            // FILETIME/DateTimeOffset
            if (managedType == typeof(DateTimeOffset))
                return (T) (object) DateTimeOffset.FromFileTime(Marshal.ReadInt64(buffer));
            */

            if (managedType == typeof(Guid))
            {
                var value = (Guid)(object)propertyValue;
                Marshal.StructureToPtr(value, buffer, false);
                propBufSize = (uint)Marshal.SizeOf(managedType);
            }

            #endregion

            if (buffer == IntPtr.Zero)
                throw new NotImplementedException("Type not supported.");

            try
            {
                var ret = SetupApiWrapper.CM_Set_DevNode_Property(
                    _instanceHandle,
                    ref nativePropKey,
                    nativePropType,
                    buffer,
                    propBufSize,
                    0
                );

                if (ret != SetupApiWrapper.ConfigManagerResult.Success)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private SetupApiWrapper.ConfigManagerResult GetProperty(
            SetupApiWrapper.DevPropKey propertyKey,
            out SetupApiWrapper.DevPropType propertyType,
            out IntPtr valueBuffer,
            out uint valueBufferSize
        )
        {
            valueBufferSize = 2018;

            valueBuffer = Marshal.AllocHGlobal((int)valueBufferSize);

            var ret = SetupApiWrapper.CM_Get_DevNode_Property(
                _instanceHandle,
                ref propertyKey,
                out propertyType,
                valueBuffer,
                ref valueBufferSize,
                0
            );

            if (ret == SetupApiWrapper.ConfigManagerResult.Success) return ret;
            Marshal.FreeHGlobal(valueBuffer);
            valueBuffer = IntPtr.Zero;
            return ret;
        }
    }
}