// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace LiteRTLM.Core
{
    public class Backend
    {
        public string Name { get; }
        public int? ThreadCount { get; }
        public string NativeLibraryDir { get; }

        private Backend(string name, int? threadCount = null, string nativeLibraryDir = null)
        {
            Name = name;
            ThreadCount = threadCount;
            NativeLibraryDir = nativeLibraryDir;
        }

        public static Backend Cpu(int? threadCount = null) => new Backend("cpu", threadCount);
        public static Backend Gpu() => new Backend("gpu");
        public static Backend Npu(string nativeLibraryDir = null) => new Backend("npu", null, nativeLibraryDir);
    }
}
