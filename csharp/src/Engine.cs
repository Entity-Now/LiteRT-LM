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
using System.Linq;
using System.Text.Json;

namespace LiteRTLM.Core
{
    public class Engine : IDisposable
    {
        private readonly object _lock = new object();
        private IntPtr _handle = IntPtr.Zero;

        public EngineConfig EngineConfig { get; }

        public bool IsInitialized => _handle != IntPtr.Zero;

        public Engine(EngineConfig engineConfig)
        {
            EngineConfig = engineConfig ?? throw new ArgumentNullException(nameof(engineConfig));
        }

        public void Initialize()
        {
            InitializeInternal(null, null);
        }

        internal void InitializeForBenchmark(int prefillTokens, int decodeTokens)
        {
            InitializeInternal(prefillTokens, decodeTokens);
        }

        private void InitializeInternal(int? benchmarkPrefillTokens, int? benchmarkDecodeTokens)
        {
            lock (_lock)
            {
                if (IsInitialized)
                {
                    throw new LiteRTLMEngineException("Engine is already initialized.");
                }

                string backendStr = EngineConfig.Backend.Name;
                string visionBackendStr = EngineConfig.VisionBackend?.Name;
                string audioBackendStr = EngineConfig.AudioBackend?.Name;

                IntPtr settings = LiteRtLmNative.litert_lm_engine_settings_create(
                    EngineConfig.ModelPath,
                    backendStr,
                    visionBackendStr,
                    audioBackendStr
                );

                if (settings == IntPtr.Zero)
                {
                    throw new LiteRTLMEngineException("Failed to create engine settings.");
                }

                try
                {
                    if (EngineConfig.MaxNumTokens.HasValue)
                    {
                        LiteRtLmNative.litert_lm_engine_settings_set_max_num_tokens(settings, EngineConfig.MaxNumTokens.Value);
                    }
                    if (!string.IsNullOrEmpty(EngineConfig.CacheDir))
                    {
                        LiteRtLmNative.litert_lm_engine_settings_set_cache_dir(settings, EngineConfig.CacheDir);
                    }

                    if (EngineConfig.LoraRank.HasValue)
                    {
                        LiteRtLmNative.litert_lm_engine_settings_set_lora_rank(settings, EngineConfig.LoraRank.Value);
                        if (EngineConfig.LoraRank.Value > 0)
                        {
                            int[] ranks = { EngineConfig.LoraRank.Value };
                            int status = LiteRtLmNative.litert_lm_engine_settings_set_supported_lora_ranks(settings, ranks, (UIntPtr)1);
                            if (status != 0)
                            {
                                throw new LiteRTLMEngineException("Failed to set supported LoRA ranks.");
                            }
                        }
                    }

                    if (EngineConfig.AudioLoraRank.HasValue)
                    {
                        LiteRtLmNative.litert_lm_engine_settings_set_audio_lora_rank(settings, EngineConfig.AudioLoraRank.Value);
                        if (EngineConfig.AudioLoraRank.Value > 0)
                        {
                            int[] ranks = { EngineConfig.AudioLoraRank.Value };
                            int status = LiteRtLmNative.litert_lm_engine_settings_set_supported_audio_lora_ranks(settings, ranks, (UIntPtr)1);
                            if (status != 0)
                            {
                                throw new LiteRTLMEngineException("Failed to set supported Audio LoRA ranks.");
                            }
                        }
                    }

                    if (benchmarkPrefillTokens.HasValue && benchmarkDecodeTokens.HasValue)
                    {
                        LiteRtLmNative.litert_lm_engine_settings_enable_benchmark(settings);
                        LiteRtLmNative.litert_lm_engine_settings_set_num_prefill_tokens(settings, benchmarkPrefillTokens.Value);
                        LiteRtLmNative.litert_lm_engine_settings_set_num_decode_tokens(settings, benchmarkDecodeTokens.Value);
                    }
                    else if (ExperimentalFlags.EnableBenchmark)
                    {
                        LiteRtLmNative.litert_lm_engine_settings_enable_benchmark(settings);
                    }

                    if (ExperimentalFlags.EnableSpeculativeDecoding.HasValue)
                    {
                        LiteRtLmNative.litert_lm_engine_settings_set_enable_speculative_decoding(settings, ExperimentalFlags.EnableSpeculativeDecoding.Value);
                    }

                    _handle = LiteRtLmNative.litert_lm_engine_create(settings);
                    if (_handle == IntPtr.Zero)
                    {
                        throw new LiteRTLMEngineException("Failed to create engine.");
                    }
                }
                finally
                {
                    LiteRtLmNative.litert_lm_engine_settings_delete(settings);
                }
            }
        }

        public Conversation CreateConversation(ConversationConfig config = null)
        {
            lock (_lock)
            {
                if (!IsInitialized)
                {
                    throw new LiteRTLMEngineException("Engine is not initialized.");
                }

                var conversationConfig = config ?? new ConversationConfig();
                
                var systemMessage = conversationConfig.SystemMessage;
                int initialSystemMessageCount = conversationConfig.InitialMessages.Count(m => m.Role == Role.System);

                if (systemMessage != null && initialSystemMessageCount > 0)
                {
                    throw new LiteRTLMConfigException("Cannot set both systemMessage and have system messages in initialMessages.");
                }
                if (initialSystemMessageCount > 1)
                {
                    throw new LiteRTLMConfigException("Cannot have multiple system messages in initialMessages.");
                }

                var toolManager = new ToolManager(conversationConfig.Tools);

                string systemMessageJsonStr = systemMessage != null ? systemMessage.ToJsonString() : string.Empty;
                string toolDescriptionJsonStr = toolManager.ToolsJsonDescription;

                string messagesJsonStr = string.Empty;
                if (conversationConfig.InitialMessages.Count > 0)
                {
                    var initialList = conversationConfig.InitialMessages.Select(m => m.ToJsonDictionary()).ToList();
                    messagesJsonStr = JsonSerializer.Serialize(initialList);
                }

                IntPtr cSessionConfig = LiteRtLmNative.litert_lm_session_config_create();
                if (cSessionConfig == IntPtr.Zero)
                {
                    throw new LiteRTLMEngineException("Failed to create session config.");
                }

                try
                {
                    if (conversationConfig.SamplerConfig != null)
                    {
                        var samplerParams = conversationConfig.SamplerConfig;
                        var paramsStruct = new LiteRtLmSamplerParams
                        {
                            type = LiteRtLmSamplerType.TopP,
                            top_k = samplerParams.TopK,
                            top_p = samplerParams.TopP,
                            temperature = samplerParams.Temperature,
                            seed = samplerParams.Seed
                        };
                        LiteRtLmNative.litert_lm_session_config_set_sampler_params(cSessionConfig, ref paramsStruct);
                    }

                    if (!string.IsNullOrEmpty(conversationConfig.LoraPath))
                    {
                        int status = LiteRtLmNative.litert_lm_session_config_set_lora_path(cSessionConfig, conversationConfig.LoraPath);
                        if (status != 0)
                        {
                            throw new LiteRTLMEngineException("Failed to set LoRA path.");
                        }
                    }

                    if (!string.IsNullOrEmpty(conversationConfig.AudioLoraPath))
                    {
                        int status = LiteRtLmNative.litert_lm_session_config_set_audio_lora_path(cSessionConfig, conversationConfig.AudioLoraPath);
                        if (status != 0)
                        {
                            throw new LiteRTLMEngineException("Failed to set Audio LoRA path.");
                        }
                    }

                    IntPtr cConversationConfig = LiteRtLmNative.litert_lm_conversation_config_create();
                    if (cConversationConfig == IntPtr.Zero)
                    {
                        throw new LiteRTLMEngineException("Failed to create conversation config.");
                    }

                    try
                    {
                        LiteRtLmNative.litert_lm_conversation_config_set_session_config(cConversationConfig, cSessionConfig);
                        if (!string.IsNullOrEmpty(systemMessageJsonStr))
                        {
                            LiteRtLmNative.litert_lm_conversation_config_set_system_message(cConversationConfig, systemMessageJsonStr);
                        }
                        if (!string.IsNullOrEmpty(toolDescriptionJsonStr))
                        {
                            LiteRtLmNative.litert_lm_conversation_config_set_tools(cConversationConfig, toolDescriptionJsonStr);
                        }
                        if (!string.IsNullOrEmpty(messagesJsonStr))
                        {
                            LiteRtLmNative.litert_lm_conversation_config_set_messages(cConversationConfig, messagesJsonStr);
                        }

                        LiteRtLmNative.litert_lm_conversation_config_set_enable_constrained_decoding(
                            cConversationConfig, ExperimentalFlags.EnableConversationConstrainedDecoding);

                        IntPtr conversationHandle = LiteRtLmNative.litert_lm_conversation_create(_handle, cConversationConfig);
                        if (conversationHandle == IntPtr.Zero)
                        {
                            throw new LiteRTLMEngineException("Failed to create conversation.");
                        }

                        return new Conversation(conversationHandle, toolManager);
                    }
                    finally
                    {
                        LiteRtLmNative.litert_lm_conversation_config_delete(cConversationConfig);
                    }
                }
                finally
                {
                    LiteRtLmNative.litert_lm_session_config_delete(cSessionConfig);
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_handle != IntPtr.Zero)
                {
                    LiteRtLmNative.litert_lm_engine_delete(_handle);
                    _handle = IntPtr.Zero;
                }
            }
            GC.SuppressFinalize(this);
        }

        ~Engine()
        {
            Dispose();
        }
    }
}
