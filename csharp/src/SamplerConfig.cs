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
    public class SamplerConfig
    {
        public int TopK { get; }
        public float TopP { get; }
        public float Temperature { get; }
        public int Seed { get; }

        public SamplerConfig(int topK, float topP, float temperature, int seed = 0)
        {
            if (topK <= 0)
            {
                throw new LiteRTLMConfigException("topK should be positive.");
            }
            if (topP < 0 || topP > 1)
            {
                throw new LiteRTLMConfigException("topP not between 0 and 1.");
            }
            if (temperature < 0)
            {
                throw new LiteRTLMConfigException("temperature should be non-negative.");
            }

            TopK = topK;
            TopP = topP;
            Temperature = temperature;
            Seed = seed;
        }
    }
}
