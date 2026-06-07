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

using System.Collections.Generic;

namespace LiteRTLM.Core
{
    public class ConversationConfig
    {
        public Message SystemMessage { get; }
        public List<Message> InitialMessages { get; }
        public List<ITool> Tools { get; }
        public SamplerConfig SamplerConfig { get; }
        public string LoraPath { get; }
        public string AudioLoraPath { get; }

        public ConversationConfig(
            Message systemMessage = null,
            List<Message> initialMessages = null,
            List<ITool> tools = null,
            SamplerConfig samplerConfig = null,
            string loraPath = null,
            string audioLoraPath = null)
        {
            SystemMessage = systemMessage;
            if (SystemMessage != null && SystemMessage.Role != Role.System)
            {
                SystemMessage = new Message(SystemMessage.Contents, Role.System, SystemMessage.Channels);
            }

            InitialMessages = initialMessages ?? new List<Message>();
            Tools = tools ?? new List<ITool>();
            SamplerConfig = samplerConfig;
            LoraPath = loraPath;
            AudioLoraPath = audioLoraPath;
        }
    }
}
