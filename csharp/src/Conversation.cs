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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
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
        private bool _disposed;

        public bool IsAlive => _handle != IntPtr.Zero && !_disposed;

        internal Conversation(IntPtr handle, ToolManager toolManager)
        {
            _handle = handle;
            _toolManager = toolManager;
        }

        public Conversation Clone()
        {
            CheckIsAlive();

            IntPtr clonedHandle = LiteRtLmNative.litert_lm_conversation_clone(_handle);
            if (clonedHandle == IntPtr.Zero)
            {
                throw new LiteRTLMConversationException("Failed to clone conversation.");
            }

            return new Conversation(clonedHandle, _toolManager);
        }

        public async Task<Message> SendMessage(Message message, Dictionary<string, object> extraContext = null)
        {
            CheckIsAlive();

            // Serialize once up front; tool-loop iterations replace the payload.
            string messageJson = message.ToJsonString();
            string extraContextJson = SerializeExtraContext(extraContext);

            for (int i = 0; i < 25; i++)
            {
                // Native call is the heavy work; avoid an extra thread-pool hop via Task.Run.
                // Callers that need off-UI scheduling should Task.Run the whole SendMessage.
                var attempt = AttemptSendMessage(messageJson, extraContextJson);

                if (attempt.HasToolCalls)
                {
                    messageJson = SerializeToolResponse(await HandleToolCalls(attempt.ToolCalls).ConfigureAwait(false));
                    continue;
                }

                if (attempt.ParsedMessage != null)
                {
                    return attempt.ParsedMessage;
                }

                throw new LiteRTLMConversationException(
                    "Invalid response from native layer: " + attempt.ResponseString,
                    attempt.ParseError);
            }

            throw new LiteRTLMConversationException("Exceeded recurring tool call limit of 25");
        }

        private sealed class SendAttempt
        {
            public string ResponseString;
            public Message ParsedMessage;
            public Exception ParseError;
            public bool HasToolCalls;
            public List<JsonElement> ToolCalls;
        }

        private SendAttempt AttemptSendMessage(string messageJson, string extraContextJson)
        {
            IntPtr optionalArgs = LiteRtLmNative.litert_lm_conversation_optional_args_create();
            if (ExperimentalFlags.VisualTokenBudget.HasValue)
            {
                LiteRtLmNative.litert_lm_conversation_optional_args_set_visual_token_budget(
                    optionalArgs, ExperimentalFlags.VisualTokenBudget.Value);
            }

            try
            {
                IntPtr responsePtr = LiteRtLmNative.litert_lm_conversation_send_message(
                    _handle,
                    messageJson,
                    extraContextJson,
                    optionalArgs);

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
                    if (string.IsNullOrEmpty(responseString))
                    {
                        throw new LiteRTLMConversationException("Native get string for response returned empty.");
                    }

                    // Single parse for tool_calls detection + Message construction.
                    return ParseSendResponse(responseString);
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

        private static SendAttempt ParseSendResponse(string responseString)
        {
            var result = new SendAttempt { ResponseString = responseString };
            try
            {
                using (var doc = JsonDocument.Parse(responseString))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("tool_calls", out var toolCallsVal) &&
                        toolCallsVal.ValueKind == JsonValueKind.Array &&
                        toolCallsVal.GetArrayLength() > 0)
                    {
                        result.HasToolCalls = true;
                        result.ToolCalls = new List<JsonElement>(toolCallsVal.GetArrayLength());
                        foreach (var item in toolCallsVal.EnumerateArray())
                        {
                            result.ToolCalls.Add(item.Clone());
                        }

                        return result;
                    }

                    result.ParsedMessage = TryJsonToMessage(root, out result.ParseError);
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.ParseError = ex;
                return result;
            }
        }

        public async IAsyncEnumerable<Message> SendMessageStream(
            Message message,
            Dictionary<string, object> extraContext = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var channel = System.Threading.Channels.Channel.CreateUnbounded<Message>(
                new System.Threading.Channels.UnboundedChannelOptions
                {
                    SingleWriter = false,
                    SingleReader = true,
                    AllowSynchronousContinuations = true
                });

            string extraContextJson = SerializeExtraContext(extraContext);
            var context = new StreamContext(channel, this, extraContext, extraContextJson);
            var gch = GCHandle.Alloc(context);
            context.GCHandle = gch;

            // Kick off native stream without awaiting — consumer drains the channel.
            // Serialize JSON on this thread to avoid racing on Message after yield.
            string messageJson = message.ToJsonString();
            ThreadPool.UnsafeQueueUserWorkItem(
                _ => StartNativeStream(messageJson, context),
                null);

            var reader = channel.Reader;
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var msg))
                {
                    if (msg != null)
                    {
                        yield return msg;
                    }
                }
            }
        }

        private void StartNativeStream(string messageJson, StreamContext context)
        {
            try
            {
                IntPtr optionalArgs = LiteRtLmNative.litert_lm_conversation_optional_args_create();
                if (ExperimentalFlags.VisualTokenBudget.HasValue)
                {
                    LiteRtLmNative.litert_lm_conversation_optional_args_set_visual_token_budget(
                        optionalArgs, ExperimentalFlags.VisualTokenBudget.Value);
                }

                try
                {
                    int status = LiteRtLmNative.litert_lm_conversation_send_message_stream(
                        _handle,
                        messageJson,
                        context.ExtraContextJson,
                        optionalArgs,
                        _streamCallback,
                        GCHandle.ToIntPtr(context.GCHandle));

                    if (status != 0)
                    {
                        context.Complete(new LiteRTLMConversationException(
                            $"Failed to start stream. Status: {status}"));
                    }
                }
                finally
                {
                    LiteRtLmNative.litert_lm_conversation_optional_args_delete(optionalArgs);
                }
            }
            catch (Exception ex)
            {
                context.Complete(ex);
            }
        }

        private static readonly LiteRtLmStreamCallback _streamCallback = StreamCallbackImpl;

        private static void StreamCallbackImpl(IntPtr callbackData, IntPtr chunkPtr)
        {
            if (callbackData == IntPtr.Zero || chunkPtr == IntPtr.Zero)
            {
                return;
            }

            GCHandle gch;
            try
            {
                gch = GCHandle.FromIntPtr(callbackData);
            }
            catch
            {
                return;
            }

            if (!gch.IsAllocated || !(gch.Target is StreamContext context) || context.IsCompleted)
            {
                return;
            }

            try
            {
                IntPtr errorPtr = LiteRtLmNative.litert_lm_stream_chunk_get_error(chunkPtr);
                if (errorPtr != IntPtr.Zero)
                {
                    string errorStr = LiteRtLmNative.PtrToStringUtf8(errorPtr) ?? "unknown native stream error";
                    context.Complete(new LiteRTLMConversationException(
                        "Invalid response from native layer: " + errorStr));
                    return;
                }

                bool isFinal = LiteRtLmNative.litert_lm_stream_chunk_is_final(chunkPtr);
                IntPtr textPtr = LiteRtLmNative.litert_lm_stream_chunk_get_text(chunkPtr);

                if (textPtr != IntPtr.Zero)
                {
                    string chunkStr = LiteRtLmNative.PtrToStringUtf8(textPtr);
                    if (!string.IsNullOrWhiteSpace(chunkStr))
                    {
                        try
                        {
                            // Single parse per chunk (tool_calls + message body).
                            using (var doc = JsonDocument.Parse(chunkStr))
                            {
                                var root = doc.RootElement;
                                if (root.ValueKind == JsonValueKind.Object &&
                                    root.TryGetProperty("tool_calls", out var toolCallsVal) &&
                                    toolCallsVal.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var item in toolCallsVal.EnumerateArray())
                                    {
                                        context.PendingToolCalls.Add(item.Clone());
                                    }
                                }

                                Message msg = TryJsonToMessage(root, out _);
                                if (msg != null)
                                {
                                    context.Channel.Writer.TryWrite(msg);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            context.Complete(new LiteRTLMConversationException(
                                "Failed to parse response JSON: " + ex.Message, ex));
                            return;
                        }
                    }
                }

                if (!isFinal)
                {
                    return;
                }

                if (context.PendingToolCalls.Count > 0)
                {
                    if (context.ToolCallCount >= 25)
                    {
                        context.Complete(new LiteRTLMConversationException(
                            "Exceeded recurring tool call limit of 25"));
                        return;
                    }

                    context.ToolCallCount++;
                    var toolCallsToRun = context.PendingToolCalls;
                    context.PendingToolCalls = new List<JsonElement>(4);

                    ThreadPool.UnsafeQueueUserWorkItem(
                        asyncState =>
                        {
                            var state = ((StreamContext ctx, List<JsonElement> calls))asyncState;
                            _ = ContinueAfterToolsAsync(state.ctx, state.calls);
                        },
                        (context, toolCallsToRun));
                }
                else
                {
                    context.Complete(null);
                }
            }
            catch (Exception ex)
            {
                try { context.Complete(ex); }
                catch { /* never throw from native callback */ }
            }
        }

        private static async Task ContinueAfterToolsAsync(StreamContext context, List<JsonElement> toolCallsToRun)
        {
            try
            {
                var toolResponseJson = await context.Conversation.HandleToolCalls(toolCallsToRun)
                    .ConfigureAwait(false);
                context.Conversation.SendToStream(toolResponseJson, context);
            }
            catch (Exception ex)
            {
                context.Complete(ex);
            }
        }

        private void SendToStream(Dictionary<string, object> toolResponseJson, StreamContext context)
        {
            if (context.IsCompleted)
            {
                return;
            }

            string messageJson = SerializeToolResponse(toolResponseJson);

            IntPtr optionalArgs = LiteRtLmNative.litert_lm_conversation_optional_args_create();
            if (ExperimentalFlags.VisualTokenBudget.HasValue)
            {
                LiteRtLmNative.litert_lm_conversation_optional_args_set_visual_token_budget(
                    optionalArgs, ExperimentalFlags.VisualTokenBudget.Value);
            }

            try
            {
                if (!context.GCHandle.IsAllocated)
                {
                    throw new LiteRTLMConversationException("Stream context was already released.");
                }

                int status = LiteRtLmNative.litert_lm_conversation_send_message_stream(
                    _handle,
                    messageJson,
                    context.ExtraContextJson,
                    optionalArgs,
                    _streamCallback,
                    GCHandle.ToIntPtr(context.GCHandle));

                if (status != 0)
                {
                    throw new LiteRTLMConversationException($"Failed to start stream. Status: {status}");
                }
            }
            catch (Exception ex)
            {
                context.Complete(ex);
            }
            finally
            {
                LiteRtLmNative.litert_lm_conversation_optional_args_delete(optionalArgs);
            }
        }

        private async Task<Dictionary<string, object>> HandleToolCalls(List<JsonElement> toolCalls)
        {
            var toolResponses = new List<Dictionary<string, object>>(toolCalls.Count);

            foreach (var toolCall in toolCalls)
            {
                if (!toolCall.TryGetProperty("function", out var functionVal) ||
                    !functionVal.TryGetProperty("name", out var nameVal) ||
                    !functionVal.TryGetProperty("arguments", out var argsVal))
                {
                    continue;
                }

                string name = nameVal.GetString();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var argsDict = new Dictionary<string, object>(StringComparer.Ordinal);
                if (argsVal.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in argsVal.EnumerateObject())
                    {
                        // Store raw JsonElement; ToolManager.ResolveValue already understands it.
                        // Clone so values outlive the original document.
                        argsDict[prop.Name] = prop.Value.Clone();
                    }
                }
                else if (argsVal.ValueKind == JsonValueKind.String)
                {
                    string argsJson = argsVal.GetString();
                    if (!string.IsNullOrEmpty(argsJson))
                    {
                        try
                        {
                            using (var argsDoc = JsonDocument.Parse(argsJson))
                            {
                                if (argsDoc.RootElement.ValueKind == JsonValueKind.Object)
                                {
                                    foreach (var prop in argsDoc.RootElement.EnumerateObject())
                                    {
                                        argsDict[prop.Name] = prop.Value.Clone();
                                    }
                                }
                            }
                        }
                        catch
                        {
                            argsDict["raw"] = argsJson;
                        }
                    }
                }

                try
                {
                    var result = await _toolManager.ExecuteAsync(name, argsDict).ConfigureAwait(false);
                    toolResponses.Add(new Dictionary<string, object>
                    {
                        { "type", "tool_response" },
                        { "name", name },
                        { "response", result }
                    });
                }
                catch (Exception ex)
                {
                    throw new LiteRTLMConversationException(
                        "Error processing tool call " + name + ": " + ex.Message, ex);
                }
            }

            return new Dictionary<string, object>
            {
                { "role", "tool" },
                { "content", toolResponses }
            };
        }

        private static string SerializeExtraContext(Dictionary<string, object> extraContext)
        {
            if (extraContext == null || extraContext.Count == 0)
            {
                return null;
            }

            return JsonSerializer.Serialize(extraContext, JsonUtil.SerializerOptions);
        }

        private static string SerializeToolResponse(Dictionary<string, object> toolResponseJson)
        {
            return JsonSerializer.Serialize(toolResponseJson, JsonUtil.SerializerOptions);
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
                throw new LiteRTLMConversationException(
                    "Benchmark flag is not enabled. Please enable the flag by setting ExperimentalFlags.EnableBenchmark to true before initializing the Engine.");
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
                    lastDecodeTokensPerSec);
            }
            finally
            {
                LiteRtLmNative.litert_lm_benchmark_info_delete(benchmarkInfoPtr);
            }
        }

        public static Message JsonToMessage(string jsonString)
        {
            Message msg = TryJsonToMessage(jsonString, out var error);
            if (msg != null)
            {
                return msg;
            }

            if (error != null)
            {
                throw error;
            }

            throw new LiteRTLMMessageException("No content or channels found in JSON string. Cannot create Message.");
        }

        public static Message TryJsonToMessage(string jsonString, out Exception error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                return null;
            }

            try
            {
                using (var doc = JsonDocument.Parse(jsonString))
                {
                    return TryJsonToMessage(doc.RootElement, out error);
                }
            }
            catch (Exception ex) when (!(ex is LiteRTLMMessageException))
            {
                error = new LiteRTLMMessageException("Failed to convert Message from JSON string.", ex);
                return null;
            }
            catch (LiteRTLMMessageException ex)
            {
                error = ex;
                return null;
            }
        }

        /// <summary>
        /// Parse from an already-materialized JSON tree (avoids a second Parse on the stream path).
        /// </summary>
        public static Message TryJsonToMessage(JsonElement root, out Exception error)
        {
            error = null;
            try
            {
                if (root.ValueKind == JsonValueKind.Null || root.ValueKind == JsonValueKind.Undefined)
                {
                    return null;
                }

                if (root.ValueKind != JsonValueKind.Object)
                {
                    if (root.ValueKind == JsonValueKind.String)
                    {
                        string plain = root.GetString();
                        return string.IsNullOrEmpty(plain) ? null : new Message(plain, Role.Model);
                    }

                    error = new LiteRTLMMessageException("Message JSON root must be an object.");
                    return null;
                }

                bool hasAnyProperty = false;
                foreach (var _ in root.EnumerateObject())
                {
                    hasAnyProperty = true;
                    break;
                }

                if (!hasAnyProperty)
                {
                    return null;
                }

                var contents = new List<Content>(2);
                if (root.TryGetProperty("content", out var contentNode))
                {
                    AppendContentNodes(contentNode, contents);
                }

                Dictionary<string, string> channels = null;
                if (root.TryGetProperty("channels", out var channelsDict) &&
                    channelsDict.ValueKind == JsonValueKind.Object)
                {
                    channels = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var prop in channelsDict.EnumerateObject())
                    {
                        channels[prop.Name] = JsonElementToString(prop.Value);
                    }
                }

                if (contents.Count == 0 && (channels == null || channels.Count == 0))
                {
                    return null;
                }

                Role role = Role.Model;
                if (root.TryGetProperty("role", out var roleEl) && roleEl.ValueKind == JsonValueKind.String)
                {
                    role = ParseRole(roleEl.GetString());
                }

                return new Message(contents, role, channels);
            }
            catch (Exception ex) when (!(ex is LiteRTLMMessageException))
            {
                error = new LiteRTLMMessageException("Failed to convert Message from JSON string.", ex);
                return null;
            }
            catch (LiteRTLMMessageException ex)
            {
                error = ex;
                return null;
            }
        }

        private static void AppendContentNodes(JsonElement contentNode, List<Content> contents)
        {
            switch (contentNode.ValueKind)
            {
                case JsonValueKind.String:
                {
                    string text = contentNode.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        contents.Add(new TextContent(text));
                    }

                    break;
                }
                case JsonValueKind.Array:
                    foreach (var item in contentNode.EnumerateArray())
                    {
                        AppendSingleContentPart(item, contents);
                    }

                    break;
                case JsonValueKind.Object:
                    AppendSingleContentPart(contentNode, contents);
                    break;
            }
        }

        private static void AppendSingleContentPart(JsonElement item, List<Content> contents)
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string text = item.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    contents.Add(new TextContent(text));
                }

                return;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            string type = null;
            if (item.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
            {
                type = typeProp.GetString();
            }

            if ((type == null || string.Equals(type, "text", StringComparison.OrdinalIgnoreCase)) &&
                item.TryGetProperty("text", out var textProp))
            {
                contents.Add(new TextContent(JsonElementToString(textProp)));
                return;
            }

            if (string.Equals(type, "image", StringComparison.OrdinalIgnoreCase))
            {
                if (item.TryGetProperty("path", out var pathProp) && pathProp.ValueKind == JsonValueKind.String)
                {
                    contents.Add(new ImageFileContent(pathProp.GetString()));
                }
                else if (item.TryGetProperty("blob", out var blobProp) && blobProp.ValueKind == JsonValueKind.String)
                {
                    try
                    {
                        contents.Add(new ImageDataContent(
                            Convert.FromBase64String(blobProp.GetString() ?? string.Empty)));
                    }
                    catch { /* ignore malformed stream image */ }
                }

                return;
            }

            if (string.Equals(type, "audio", StringComparison.OrdinalIgnoreCase))
            {
                if (item.TryGetProperty("path", out var pathProp) && pathProp.ValueKind == JsonValueKind.String)
                {
                    contents.Add(new AudioFileContent(pathProp.GetString()));
                }
                else if (item.TryGetProperty("blob", out var blobProp) && blobProp.ValueKind == JsonValueKind.String)
                {
                    try
                    {
                        contents.Add(new AudioDataContent(
                            Convert.FromBase64String(blobProp.GetString() ?? string.Empty)));
                    }
                    catch { /* ignore malformed stream audio */ }
                }
            }
        }

        private static string JsonElementToString(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.String:
                    return el.GetString() ?? string.Empty;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return el.GetRawText();
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return string.Empty;
                default:
                    return el.GetRawText();
            }
        }

        private static Role ParseRole(string role)
        {
            if (string.IsNullOrEmpty(role))
            {
                return Role.Model;
            }

            // Avoid alloc from ToLowerInvariant on hot path: compare ordinal ignore case.
            if (role.Equals("user", StringComparison.OrdinalIgnoreCase)) return Role.User;
            if (role.Equals("system", StringComparison.OrdinalIgnoreCase)) return Role.System;
            if (role.Equals("tool", StringComparison.OrdinalIgnoreCase)) return Role.Tool;
            return Role.Model;
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
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_handle != IntPtr.Zero)
            {
                LiteRtLmNative.litert_lm_conversation_delete(_handle);
                _handle = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);
        }

        private sealed class StreamContext
        {
            private int _completed;

            public System.Threading.Channels.Channel<Message> Channel { get; }
            public Conversation Conversation { get; }
            public Dictionary<string, object> ExtraContext { get; }
            /// <summary>Pre-serialized extra context; reused across tool follow-up streams.</summary>
            public string ExtraContextJson { get; }
            public GCHandle GCHandle { get; set; }
            public int ToolCallCount { get; set; }
            public List<JsonElement> PendingToolCalls { get; set; } = new List<JsonElement>(4);

            public bool IsCompleted => Volatile.Read(ref _completed) != 0;

            public StreamContext(
                System.Threading.Channels.Channel<Message> channel,
                Conversation conversation,
                Dictionary<string, object> extraContext,
                string extraContextJson)
            {
                Channel = channel;
                Conversation = conversation;
                ExtraContext = extraContext;
                ExtraContextJson = extraContextJson;
            }

            public void Complete(Exception error)
            {
                if (Interlocked.Exchange(ref _completed, 1) != 0)
                {
                    return;
                }

                try
                {
                    if (error != null)
                    {
                        Channel.Writer.TryComplete(error);
                    }
                    else
                    {
                        Channel.Writer.TryComplete();
                    }
                }
                finally
                {
                    try
                    {
                        if (GCHandle.IsAllocated)
                        {
                            GCHandle.Free();
                        }
                    }
                    catch
                    {
                        // ignore double-free races
                    }
                }
            }
        }
    }
}
