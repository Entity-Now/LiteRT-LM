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

using System;

namespace LiteRTLM.Core
{
    public class Capabilities : IDisposable
    {
        private IntPtr _handle;

        public Capabilities(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath))
            {
                throw new ArgumentException("Model path must be specified.", nameof(modelPath));
            }

            _handle = LiteRtLmNative.litert_lm_loaded_file_create(modelPath);
            if (_handle == IntPtr.Zero)
            {
                throw new LiteRTLMEngineException($"Failed to load LiteRT-LM file for capability checks: {modelPath}");
            }
        }

        public bool HasSpeculativeDecodingSupport()
        {
            if (_handle == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(Capabilities));
            }
            return LiteRtLmNative.litert_lm_loaded_file_has_speculative_decoding_support(_handle);
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                LiteRtLmNative.litert_lm_loaded_file_delete(_handle);
                _handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~Capabilities()
        {
            Dispose();
        }
    }
}
