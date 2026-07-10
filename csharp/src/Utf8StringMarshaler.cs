// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LiteRTLM.Core
{
    /// <summary>
    /// Marshals managed strings as null-terminated UTF-8 for the litert-lm C API.
    /// Prefer this over <see cref="UnmanagedType.LPStr"/> (ANSI) which corrupts non-ASCII text
    /// and forces an extra encoding hop.
    /// </summary>
    internal sealed class Utf8StringMarshaler : ICustomMarshaler
    {
        private static readonly Utf8StringMarshaler Instance = new Utf8StringMarshaler();

        public static ICustomMarshaler GetInstance(string cookie) => Instance;

        public void CleanUpManagedData(object managedObj)
        {
        }

        public void CleanUpNativeData(IntPtr pNativeData)
        {
            if (pNativeData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pNativeData);
            }
        }

        public int GetNativeDataSize() => -1;

        public IntPtr MarshalManagedToNative(object managedObj)
        {
            if (managedObj == null)
            {
                return IntPtr.Zero;
            }

            string value = (string)managedObj;
            int byteCount = Encoding.UTF8.GetByteCount(value);
            byte[] buffer = new byte[byteCount + 1]; // +1 NUL
            Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, 0);
            IntPtr ptr = Marshal.AllocHGlobal(buffer.Length);
            Marshal.Copy(buffer, 0, ptr, buffer.Length);
            return ptr;
        }

        public object MarshalNativeToManaged(IntPtr pNativeData)
        {
            return LiteRtLmNative.PtrToStringUtf8(pNativeData);
        }
    }
}
