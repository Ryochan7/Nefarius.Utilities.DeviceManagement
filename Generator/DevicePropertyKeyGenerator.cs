﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using Windows.Win32;
using Windows.Win32.Devices.Properties;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DeviceManagementPropertiesGenerator
{
    [Generator]
    public class DevicePropertyKeyGenerator : ISourceGenerator
    {
        private static readonly IDictionary<uint, Type> NativeToManagedTypeMap =
            new Dictionary<uint, Type>
            {
                { PInvoke.DEVPROP_TYPE_SBYTE, typeof(sbyte) },
                { PInvoke.DEVPROP_TYPE_BYTE, typeof(byte) },
                { PInvoke.DEVPROP_TYPE_INT16, typeof(short) },
                { PInvoke.DEVPROP_TYPE_UINT16, typeof(ushort) },
                { PInvoke.DEVPROP_TYPE_INT32, typeof(int) },
                { PInvoke.DEVPROP_TYPE_UINT32, typeof(uint) },
                { PInvoke.DEVPROP_TYPE_INT64, typeof(long) },
                { PInvoke.DEVPROP_TYPE_UINT64, typeof(ulong) },
                { PInvoke.DEVPROP_TYPE_FLOAT, typeof(float) },
                { PInvoke.DEVPROP_TYPE_DOUBLE, typeof(double) },
                { PInvoke.DEVPROP_TYPE_DECIMAL, typeof(decimal) },
                { PInvoke.DEVPROP_TYPE_GUID, typeof(Guid) },
                // DEVPROP_TYPE_CURRENCY
                { PInvoke.DEVPROP_TYPE_DATE, typeof(DateTime) },
                { PInvoke.DEVPROP_TYPE_FILETIME, typeof(DateTimeOffset) },
                { PInvoke.DEVPROP_TYPE_BOOLEAN, typeof(bool) },
                { PInvoke.DEVPROP_TYPE_STRING, typeof(string) },
                { PInvoke.DEVPROP_TYPE_STRING | PInvoke.DEVPROP_TYPEMOD_LIST, typeof(string[]) },
                // DEVPROP_TYPE_SECURITY_DESCRIPTOR
                // DEVPROP_TYPE_SECURITY_DESCRIPTOR_STRING
                { PInvoke.DEVPROP_TYPE_DEVPROPKEY, typeof(DEVPROPKEY) },
                { PInvoke.DEVPROP_TYPE_DEVPROPTYPE, typeof(uint) },
                { PInvoke.DEVPROP_TYPE_BYTE | PInvoke.DEVPROP_TYPEMOD_ARRAY, typeof(byte[]) },
                { PInvoke.DEVPROP_TYPE_ERROR, typeof(int) },
                { PInvoke.DEVPROP_TYPE_NTSTATUS, typeof(int) }
                // DEVPROP_TYPE_STRING_INDIRECT
            };

        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            string devpkeyHeader =
                "https://raw.githubusercontent.com/microsoft/win32metadata/main/generation/WinSDK/RecompiledIdlHeaders/shared/devpkey.h";

            WebClient wc = new WebClient();

            string header = wc.DownloadString(devpkeyHeader);

            Regex parseRegex = new Regex(@"^DEFINE_DEVPROPKEY\((\w*), .*\/\/ (\w*)$", RegexOptions.Multiline);

            MatchCollection matches = parseRegex.Matches(header);

            Dictionary<string, Type> nameToManagedTypeMap = new Dictionary<string, Type>();

            foreach (Match match in matches)
            {
                string propName = match.Groups[1].Value;
                string typeName = match.Groups[2].Value;

                // handle special cases
                switch (typeName)
                {
                    // combined type doesn't exist as constant
                    case "DEVPROP_TYPE_STRING_LIST":
                        nameToManagedTypeMap.Add(propName, typeof(string[]));
                        break;
                    // combined type doesn't exist as constant
                    case "DEVPROP_TYPE_BINARY":
                        nameToManagedTypeMap.Add(propName, typeof(byte[]));
                        break;
                    default:
                        // exists as a comment but not as an actual constant
                        if (Equals(typeName, "DEVPROP_TYPE_BOOL"))
                        {
                            typeName = "DEVPROP_TYPE_BOOLEAN";
                        }

                        FieldInfo nativeType =
                            typeof(PInvoke).GetField(typeName, BindingFlags.Static | BindingFlags.NonPublic);
                        uint nativeTypeValue = (uint)nativeType.GetValue(null);

                        // unsupported type hit
                        if (!NativeToManagedTypeMap.ContainsKey(nativeTypeValue))
                        {
                            continue;
                        }

                        Type managedType = NativeToManagedTypeMap[nativeTypeValue];

                        nameToManagedTypeMap.Add(propName, managedType);
                        break;
                }
            }

            List<FieldInfo> allProperties = typeof(PInvoke)
                .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(info => info.FieldType == typeof(DEVPROPKEY))
                .ToList();

            StringBuilder sb = new StringBuilder(@"using System;
using Windows.Win32;
using Windows.Win32.Devices.Properties;

namespace Nefarius.Utilities.DeviceManagement.PnP
{
    public partial class DevicePropertyKey
    {");
            foreach (FieldInfo property in allProperties)
            {
                // strip prefix
                string fieldName = property.Name.Replace("DEVPKEY_", string.Empty);
                string propertyName = property.Name;

                // skip if we don't know the managed type
                if (!nameToManagedTypeMap.ContainsKey(propertyName))
                {
                    continue;
                }

                Type managedType = nameToManagedTypeMap[propertyName];

                sb.AppendLine($@"
        /// <summary>
        ///     {propertyName}
        /// </summary>
        public static readonly DevicePropertyKey {fieldName} = new DevicePropertyKey(PInvoke.{propertyName}, typeof({managedType.Name}));");
            }

            sb.Append(@"
    }
}
");

            context.AddSource("DevicePropertyKeysGenerated", SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }
}