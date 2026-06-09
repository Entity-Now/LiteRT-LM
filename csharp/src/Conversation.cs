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
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace LiteRTLM.Core
{
    public class BenchmarkInfo
    {
        public double InitTimeInSecond { get; }
        public double TimeToFirstTokenInSecond { get; }
        public int LastPrefillTokenCount { get; }
        public int LastDecodeTokenCount { get; }
        public double LastPrefillTokensPerSecond { get; }
        public double LastDecodeTokensPerSecond { get; }

        public BenchmarkInfo(
            double initTimeInSecond,
            double timeToFirstTokenInSecond,
            int lastPrefillTokenCount,
            int lastDecodeTokenCount,
            double lastPrefillTokensPerSecond,
            double lastDecodeTokensPerSecond)
        {
            InitTimeInSecond = initTimeInSecond;
            TimeToFirstTokenInSecond = timeToFirstTokenInSecond;
            LastPrefillTokenCount = lastPrefillTokenCount;
            LastDecodeTokenCount = lastDecodeTokenCount;
            LastPrefillTokensPerSecond = lastPrefillTokensPerSecond;
            LastDecodeTokensPerSecond = lastDecodeTokensPerSecond;
        }
    }

    public class Conversation : IDisposable
    {
        private IntPtr _handle;
        private readonly ToolManager _toolManager;

        public bool IsAlive => _handle != IntPtr.Zero;

        internal Conversation(IntPtr handle, ToolManager toolManager)
        {
            _handle = handle;
            _toolManager = toolManager;
        }

        public async Task<Message> SendMessage(Message message, Dictionary<string, object> extraContext = null)
        {
            CheckIsAlive();

            var currentMessageJson = message.ToJsonDictionary();

            for (int i = 0; i < 25; i++)
            {
                var (responseJson, responseString) = await Task.Run(() => AttemptSendMessage(currentMessageJson, extraContext)).ConfigureAwait(false);

                if (responseJson.TryGetProperty("tool_calls", out var toolCallsVal) && toolCallsVal.ValueKind == JsonValueKind.Array)
                {
                    var toolCallsList = toolCallsVal.EnumerateArray().ToList();
                    currentMessageJson = await HandleToolCalls(toolCallsList);
                }
                else
                {
                    if (responseJson.TryGetProperty("content", out _) || responseJson.TryGetProperty("channels", out _))
                    {
                        return JsonToMessage(responseString);
                    }
                    throw new LiteRTLMConversationException($"Invalid response from native layer: {responseString}");
                }
            }

            throw new LiteRTLMConversationException("Exceeded recurring tool call limit of 25");
        }

        private (JsonElement responseJson, string responseString) AttemptSendMessage(
            Dictionary<string, object> messageJson,
            Dictionary<string, object> extraContext)
        {
            string messageString = JsonSerializer.Serialize(messageJson);
            string extraContextString = extraContext != null ? JsonSerializer.Serialize(extraContext) : null;

            IntPtr optionalArgs = LiteRtLmNative.litert_lm_conversation_optional_args_create();
            if (ExperimentalFlags.VisualTokenBudget.HasValue)
            {
                LiteRtLmNative.litert_lm_conversation_optional_args_set_visual_token_budget(optionalArgs, ExperimentalFlags.VisualTokenBudget.Value);
            }

            try
            {
                IntPtr responsePtr = LiteRtLmNative.litert_lm_conversation_send_message(
                    _handle,
                    messageString,
                    extraContextString,
                    optionalArgs
                );

                if (responsePtr == IntPtr.Zero)
                {
                    throw new LiteRTLMConversationException("Native sendMessage returned null.");
                }

                try
                {
                    IntPtr responseChars = LiteRtLmNative.litert_lm_json_response_get_string(responsePtr);
                    if (responseChars == IntPtr.Zero)
                    {
                        throw new LiteRTLMConversationException("Native get string for response returned null.");
                    }

                    string responseString = LiteRtLmNative.PtrToStringUtf8(responseChars);
                    using (var doc = JsonDocument.Parse(responseString))
                    {
                        return (doc.RootElement.Clone(), responseString);
                    }
                }
                finally
                {
                    LiteRtLmNative.litert_lm_json_response_delete(responsePtr);
                }
            }
            finally
            {
                LiteRtLmNative.litert_lm_conversation_optional_args_delete(optionalArgs);
            }
        }

        public async IAsyncEnumerable<Message> SendMessageStream(
            Message message,
            Dictionary<string, object> extraContext = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
        {
            var channel = System.Threading.Channels.Channel.CreateUnbounded<Message>(new System.Threading.Channels.UnboundedChannelOptions
            {
                SingleWriter = true,
                SingleReader = true
            });

            var context = new StreamContext(channel, this, extraContext);
            var gch = GCHandle.Alloc(context);
            context.GCHandle = gch;

            _ = Task.Run(() =>
            {
                try
                {
                    string messageJson = message.ToJsonString();
                    string extraContextJson = extraContext != null ? JsonSerializer.Serialize(extraContext) : null;

                    IntPtr optionalArgs = LiteRtLmNative.litert_lm_conversation_optional_args_create();
                    if (ExperimentalFlags.VisualTokenBudget.HasValue)
                    {
                        LiteRtLmNative.litert_lm_conversation_optional_args_set_visual_token_budget(optionalArgs, ExperimentalFlags.VisualTokenBudget.Value);
                    }

                    try
                    {
                        int status = LiteRtLmNative.litert_lm_conversation_send_message_stream(
                            _handle,
                            messageJson,
                            extraContextJson,
                            optionalArgs,
                            _streamCallback,
                            GCHandle.ToIntPtr(gch)
                        );

                        if (status != 0)
                        {
                            gch.Free();
                            channel.Writer.TryComplete(new LiteRTLMConversationException($"Failed to start stream. Status: {status}"));
                        }
                    }
                    finally
                    {
                        LiteRtLmNative.litert_lm_conversation_optional_args_delete(optionalArgs);
                    }
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            });

            var reader = channel.Reader;
            while (await reader.WaitToReadAsync(cancellationToken))
            {
                while (reader.TryRead(out var msg))
                {
                    yield return msg;
                }
            }
        }

        private static readonly LiteRtLmStreamCallback _streamCallback = StreamCallbackImpl;

        private static void StreamCallbackImpl(
            IntPtr callbackData,
            IntPtr chunkPtr,
            bool isFinal,
            IntPtr errorMsgPtr)
        {
            if (callbackData == IntPtr.Zero) return;

            var gch = GCHandle.FromIntPtr(callbackData);
            if (!gch.IsAllocated) return;

            var context = (StreamContext)gch.Target;

            if (errorMsgPtr != IntPtr.Zero)
            {
                string errorStr = LiteRtLmNative.PtrToStringUtf8(errorMsgPtr);
                context.Channel.Writer.TryComplete(new LiteRTLMConversationException($"Invalid response from native layer: {errorStr}"));
                gch.Free();
                return;
            }

            if (chunkPtr != IntPtr.Zero)
            {
                string chunkStr = LiteRtLmNative.PtrToStringUtf8(chunkPtr);
                try
                {
                    using (var doc = JsonDocument.Parse(chunkStr))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("tool_calls", out var toolCallsVal) && toolCallsVal.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in toolCallsVal.EnumerateArray())
                            {
                                context.PendingToolCalls.Add(item.Clone());
                            }
                        }

                        if (root.TryGetProperty("content", out _) || root.TryGetProperty("channels", out _))
                        {
                            var msg = JsonToMessage(chunkStr);
                            context.Channel.Writer.TryWrite(msg);
                        }
                    }
                }
                catch (Exception ex)
                {
                    context.Channel.Writer.TryComplete(new LiteRTLMConversationException("Failed to parse response JSON: " + ex.Message, ex));
                    gch.Free();
                    return;
                }
            }

            if (isFinal)
            {
                if (context.PendingToolCalls.Count > 0)
                {
                    if (context.ToolCallCount >= 25)
                    {
                        context.Channel.Writer.TryComplete(new LiteRTLMConversationException("Exceeded recurring tool call limit of 25"));
                        gch.Free();
                        return;
                    }

                    context.ToolCallCount++;
                    var toolCallsToRun = context.PendingToolCalls.ToList();
                    context.PendingToolCalls.Clear();

                    Task.Run(async () =>
                    {
                        try
                        {
                            var toolResponseJson = await context.Conversation.HandleToolCalls(toolCallsToRun);
                            context.Conversation.SendToStream(toolResponseJson, context);
                        }
                        catch (Exception ex)
                        {
                            context.Channel.Writer.TryComplete(ex);
                            gch.Free();
                        }
                    });
                }
                else
                {
                    context.Channel.Writer.TryComplete();
                    gch.Free();
                }
            }
        }

        private void SendToStream(Dictionary<string, object> toolResponseJson, StreamContext context)
        {
            string messageJson = JsonSerializer.Serialize(toolResponseJson);
            string extraContextJson = context.ExtraContext != null ? JsonSerializer.Serialize(context.ExtraContext) : null;

            IntPtr optionalArgs = LiteRtLmNative.litert_lm_conversation_optional_args_create();
            if (ExperimentalFlags.VisualTokenBudget.HasValue)
            {
                LiteRtLmNative.litert_lm_conversation_optional_args_set_visual_token_budget(optionalArgs, ExperimentalFlags.VisualTokenBudget.Value);
            }

            try
            {
                int status = LiteRtLmNative.litert_lm_conversation_send_message_stream(
                    _handle,
                    messageJson,
                    extraContextJson,
                    optionalArgs,
                    _streamCallback,
                    GCHandle.ToIntPtr(context.GCHandle)
                );

                if (status != 0)
                {
                    throw new LiteRTLMConversationException($"Failed to start stream. Status: {status}");
                }
            }
            finally
            {
                LiteRtLmNative.litert_lm_conversation_optional_args_delete(optionalArgs);
            }
        }

        private async Task<Dictionary<string, object>> HandleToolCalls(List<JsonElement> toolCalls)
        {
            var toolResponses = new List<Dictionary<string, object>>();

            foreach (var toolCall in toolCalls)
            {
                if (toolCall.TryGetProperty("function", out var functionVal) &&
                    functionVal.TryGetProperty("name", out var nameVal) &&
                    functionVal.TryGetProperty("arguments", out var argsVal))
                {
                    string name = nameVal.GetString();
                    
                    var argsDict = new Dictionary<string, object>();
                    if (argsVal.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in argsVal.EnumerateObject())
                        {
                            argsDict[prop.Name] = prop.Value;
                        }
                    }

                    try
                    {
                        var result = await _toolManager.ExecuteAsync(name, argsDict);
                        toolResponses.Add(new Dictionary<string, object>
                        {
                            { "type", "tool_response" },
                            { "name", name },
                            { "response", result }
                        });
                    }
                    catch (Exception ex)
                    {
                        throw new LiteRTLMConversationException($"Error processing tool call {name}: {ex.Message}", ex);
                    }
                }
            }

            return new Dictionary<string, object>
            {
                { "role", "tool" },
                { "content", toolResponses }
            };
        }

        public string RenderMessageIntoString(Message message)
        {
            CheckIsAlive();
            string messageJson = message.ToJsonString();
            IntPtr cString = LiteRtLmNative.litert_lm_conversation_render_message_to_string(_handle, messageJson);
            if (cString == IntPtr.Zero)
            {
                throw new LiteRTLMConversationException("Failed to render message into string.");
            }
            return LiteRtLmNative.PtrToStringUtf8(cString);
        }

        public void Cancel()
        {
            CheckIsAlive();
            LiteRtLmNative.litert_lm_conversation_cancel_process(_handle);
        }

        public int GetTokenCount()
        {
            CheckIsAlive();
            return LiteRtLmNative.litert_lm_conversation_get_token_count(_handle);
        }

        public BenchmarkInfo GetBenchmarkInfo()
        {
            CheckIsAlive();

            if (!ExperimentalFlags.EnableBenchmark)
            {
                throw new LiteRTLMConversationException("Benchmark flag is not enabled. Please enable the flag by setting ExperimentalFlags.EnableBenchmark to true before initializing the Engine.");
            }

            IntPtr benchmarkInfoPtr = LiteRtLmNative.litert_lm_conversation_get_benchmark_info(_handle);
            if (benchmarkInfoPtr == IntPtr.Zero)
            {
                throw new LiteRTLMConversationException("Failed to get benchmark info.");
            }

            try
            {
                int numPrefillTurns = LiteRtLmNative.litert_lm_benchmark_info_get_num_prefill_turns(benchmarkInfoPtr);
                int numDecodeTurns = LiteRtLmNative.litert_lm_benchmark_info_get_num_decode_turns(benchmarkInfoPtr);

                double initTimeInSecond = LiteRtLmNative.litert_lm_benchmark_info_get_total_init_time_in_second(benchmarkInfoPtr);
                double timeToFirstTokenInSecond = LiteRtLmNative.litert_lm_benchmark_info_get_time_to_first_token(benchmarkInfoPtr);

                int lastPrefillTokenCount = numPrefillTurns > 0
                    ? LiteRtLmNative.litert_lm_benchmark_info_get_prefill_token_count_at(benchmarkInfoPtr, numPrefillTurns - 1)
                    : 0;
                double lastPrefillTokensPerSec = numPrefillTurns > 0
                    ? LiteRtLmNative.litert_lm_benchmark_info_get_prefill_tokens_per_sec_at(benchmarkInfoPtr, numPrefillTurns - 1)
                    : 0.0;

                int lastDecodeTokenCount = numDecodeTurns > 0
                    ? LiteRtLmNative.litert_lm_benchmark_info_get_decode_token_count_at(benchmarkInfoPtr, numDecodeTurns - 1)
                    : 0;
                double lastDecodeTokensPerSec = numDecodeTurns > 0
                    ? LiteRtLmNative.litert_lm_benchmark_info_get_decode_tokens_per_sec_at(benchmarkInfoPtr, numDecodeTurns - 1)
                    : 0.0;

                return new BenchmarkInfo(
                    initTimeInSecond,
                    timeToFirstTokenInSecond,
                    lastPrefillTokenCount,
                    lastDecodeTokenCount,
                    lastPrefillTokensPerSec,
                    lastDecodeTokensPerSec
                );
            }
            finally
            {
                LiteRtLmNative.litert_lm_benchmark_info_delete(benchmarkInfoPtr);
            }
        }

        public static Message JsonToMessage(string jsonString)
        {
            try
            {
                using (var doc = JsonDocument.Parse(jsonString))
                {
                    var root = doc.RootElement;
                    var contents = new List<Content>();
                    if (root.TryGetProperty("content", out var contentArray) && contentArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in contentArray.EnumerateArray())
                        {
                            if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "text" &&
                                item.TryGetProperty("text", out var textProp))
                            {
                                contents.Add(new TextContent(textProp.GetString()));
                            }
                        }
                    }

                    var channels = new Dictionary<string, string>();
                    if (root.TryGetProperty("channels", out var channelsDict) && channelsDict.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in channelsDict.EnumerateObject())
                        {
                            channels[prop.Name] = prop.Value.GetString();
                        }
                    }

                    if (contents.Count == 0 && channels.Count == 0)
                    {
                        throw new LiteRTLMMessageException("No content or channels found in JSON string. Cannot create Message.");
                    }

                    return new Message(contents, Role.Model, channels);
                }
            }
            catch (Exception ex) when (!(ex is LiteRTLMMessageException))
            {
                throw new LiteRTLMMessageException("Failed to convert Message from JSON string.", ex);
            }
        }

        private void CheckIsAlive()
        {
            if (!IsAlive)
            {
                throw new LiteRTLMConversationException("Conversation is not alive.");
            }
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                LiteRtLmNative.litert_lm_conversation_delete(_handle);
                _handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~Conversation()
        {
            Dispose();
        }

        private class StreamContext
        {
            public System.Threading.Channels.Channel<Message> Channel { get; }
            public Conversation Conversation { get; }
            public Dictionary<string, object> ExtraContext { get; }
            public GCHandle GCHandle { get; set; }
            public int ToolCallCount { get; set; }
            public List<JsonElement> PendingToolCalls { get; } = new List<JsonElement>();

            public StreamContext(System.Threading.Channels.Channel<Message> channel, Conversation conversation, Dictionary<string, object> extraContext)
            {
                Channel = channel;
                Conversation = conversation;
                ExtraContext = extraContext;
            }
        }
    }
}
