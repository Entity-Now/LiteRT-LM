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
    public class EngineConfig
    {
        public string ModelPath { get; }
        public Backend Backend { get; }
        public Backend VisionBackend { get; }
        public Backend AudioBackend { get; }
        public int? MaxNumTokens { get; }
        public string CacheDir { get; }
        public int? LoraRank { get; }
        public int? AudioLoraRank { get; }

        public EngineConfig(
            string modelPath,
            Backend backend = null,
            Backend visionBackend = null,
            Backend audioBackend = null,
            int? maxNumTokens = null,
            string cacheDir = null,
            int? loraRank = null,
            int? audioLoraRank = null)
        {
            if (string.IsNullOrEmpty(modelPath))
            {
                throw new ArgumentException("Model path must be specified.", nameof(modelPath));
            }
            if (maxNumTokens.HasValue && maxNumTokens.Value <= 0)
            {
                throw new LiteRTLMConfigException("maxNumTokens must be positive or null.");
            }

            ModelPath = modelPath;
            Backend = backend ?? Backend.Cpu();
            VisionBackend = visionBackend;
            AudioBackend = audioBackend;
            MaxNumTokens = maxNumTokens;
            CacheDir = cacheDir;
            LoraRank = loraRank;
            AudioLoraRank = audioLoraRank;
        }
    }
}
