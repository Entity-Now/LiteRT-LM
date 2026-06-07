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
using System.Runtime.InteropServices;
using System.Text;

namespace LiteRTLM.Core
{
    public enum LiteRtLmTokenUnionType
    {
        String = 0,
        Ids = 1
    }

    public enum LiteRtLmSamplerType
    {
        Unspecified = 0,
        TopK = 1,
        TopP = 2,
        Greedy = 3
    }

    public enum LiteRtLmInputDataType
    {
        Text = 0,
        Image = 1,
        ImageEnd = 2,
        Audio = 3,
        AudioEnd = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LiteRtLmSamplerParams
    {
        public LiteRtLmSamplerType type;
        public int top_k;
        public float top_p;
        public float temperature;
        public int seed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LiteRtLmInputData
    {
        public LiteRtLmInputDataType type;
        public IntPtr data;
        public UIntPtr size;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LiteRtLmStreamCallback(
        IntPtr callbackData,
        IntPtr chunkPtr,
        [MarshalAs(UnmanagedType.U1)] bool isFinal,
        IntPtr errorMsgPtr
    );

    internal static class LiteRtLmNative
    {
        private const string DllName = "CLiteRTLM";

        // Helper to marshal UTF-8 strings safely on all platform frameworks (.NET Standard 2.0 / modern .NET)
        public static string PtrToStringUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
#if NETCOREAPP
            return Marshal.PtrToStringUTF8(ptr);
#else
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0)
            {
                len++;
            }
            byte[] buffer = new byte[len];
            Marshal.Copy(ptr, buffer, 0, len);
            return Encoding.UTF8.GetString(buffer);
#endif
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_session_config_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_session_config_set_max_output_tokens(
            IntPtr config, int max_output_tokens);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_session_config_set_apply_prompt_template(
            IntPtr config, [MarshalAs(UnmanagedType.U1)] bool apply_prompt_template);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_session_config_set_sampler_params(
            IntPtr config, ref LiteRtLmSamplerParams sampler_params);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_session_config_delete(IntPtr config);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int litert_lm_session_config_set_lora_path(
            IntPtr config, [MarshalAs(UnmanagedType.LPStr)] string lora_path);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int litert_lm_session_config_set_audio_lora_path(
            IntPtr config, [MarshalAs(UnmanagedType.LPStr)] string audio_lora_path);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_conversation_config_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_conversation_config_set_session_config(
            IntPtr config, IntPtr session_config);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_conversation_config_set_system_message(
            IntPtr config, [MarshalAs(UnmanagedType.LPStr)] string system_message_json);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_conversation_config_set_tools(
            IntPtr config, [MarshalAs(UnmanagedType.LPStr)] string tools_json);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_conversation_config_set_messages(
            IntPtr config, [MarshalAs(UnmanagedType.LPStr)] string messages_json);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_conversation_config_set_extra_context(
            IntPtr config, [MarshalAs(UnmanagedType.LPStr)] string extra_context_json);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_conversation_config_set_enable_constrained_decoding(
            IntPtr config, [MarshalAs(UnmanagedType.U1)] bool enable_constrained_decoding);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_conversation_config_set_filter_channel_content_from_kv_cache(
            IntPtr config, [MarshalAs(UnmanagedType.U1)] bool filter_channel_content_from_kv_cache);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_conversation_config_delete(IntPtr config);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_conversation_optional_args_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_conversation_optional_args_delete(IntPtr optional_args);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_conversation_optional_args_set_visual_token_budget(
            IntPtr optional_args, int visual_token_budget);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_conversation_optional_args_set_max_output_tokens(
            IntPtr optional_args, int max_output_tokens);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_set_min_log_level(int level);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_engine_settings_create(
            [MarshalAs(UnmanagedType.LPStr)] string model_path,
            [MarshalAs(UnmanagedType.LPStr)] string backend_str,
            [MarshalAs(UnmanagedType.LPStr)] string vision_backend_str,
            [MarshalAs(UnmanagedType.LPStr)] string audio_backend_str);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_engine_settings_delete(IntPtr settings);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_engine_settings_set_max_num_tokens(
            IntPtr settings, int max_num_tokens);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_engine_settings_set_parallel_file_section_loading(
            IntPtr settings, [MarshalAs(UnmanagedType.U1)] bool parallel_file_section_loading);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_engine_settings_set_max_num_images(
            IntPtr settings, int max_num_images);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_engine_settings_set_cache_dir(
            IntPtr settings, [MarshalAs(UnmanagedType.LPStr)] string cache_dir);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_engine_settings_set_litert_dispatch_lib_dir(
            IntPtr settings, [MarshalAs(UnmanagedType.LPStr)] string lib_dir);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_engine_settings_set_activation_data_type(
            IntPtr settings, int activation_data_type_int);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_engine_settings_set_prefill_chunk_size(
            IntPtr settings, int prefill_chunk_size);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_engine_settings_enable_benchmark(IntPtr settings);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_engine_settings_set_num_prefill_tokens(
            IntPtr settings, int num_prefill_tokens);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_engine_settings_set_num_decode_tokens(
            IntPtr settings, int num_decode_tokens);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_engine_settings_set_enable_speculative_decoding(
            IntPtr settings, [MarshalAs(UnmanagedType.U1)] bool enable_speculative_decoding);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_engine_settings_set_lora_rank(
            IntPtr settings, int lora_rank);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int litert_lm_engine_settings_set_supported_lora_ranks(
            IntPtr settings, int[] lora_ranks, UIntPtr num_ranks);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_engine_settings_set_audio_lora_rank(
            IntPtr settings, int lora_rank);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int litert_lm_engine_settings_set_supported_audio_lora_ranks(
            IntPtr settings, int[] lora_ranks, UIntPtr num_ranks);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_engine_create(IntPtr settings);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_engine_delete(IntPtr engine);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_conversation_create(IntPtr engine, IntPtr config);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_conversation_delete(IntPtr conversation);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_conversation_clone(IntPtr conversation);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_conversation_send_message(
            IntPtr conversation,
            [MarshalAs(UnmanagedType.LPStr)] string message_json,
            [MarshalAs(UnmanagedType.LPStr)] string extra_context_json,
            IntPtr optional_args);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_json_response_delete(IntPtr response);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_json_response_get_string(IntPtr response);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int litert_lm_conversation_send_message_stream(
            IntPtr conversation,
            [MarshalAs(UnmanagedType.LPStr)] string message_json,
            [MarshalAs(UnmanagedType.LPStr)] string extra_context_json,
            IntPtr optional_args,
            LiteRtLmStreamCallback callback,
            IntPtr callback_data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_conversation_render_message_to_string(
            IntPtr conversation, [MarshalAs(UnmanagedType.LPStr)] string message_json);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_conversation_cancel_process(IntPtr conversation);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_conversation_get_benchmark_info(IntPtr conversation);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int litert_lm_conversation_get_token_count(IntPtr conversation);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_benchmark_info_delete(IntPtr benchmark_info);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern double litert_lm_benchmark_info_get_time_to_first_token(
            IntPtr benchmark_info);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern double litert_lm_benchmark_info_get_total_init_time_in_second(
            IntPtr benchmark_info);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int litert_lm_benchmark_info_get_num_prefill_turns(
            IntPtr benchmark_info);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int litert_lm_benchmark_info_get_num_decode_turns(
            IntPtr benchmark_info);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int litert_lm_benchmark_info_get_prefill_token_count_at(
            IntPtr benchmark_info, int index);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int litert_lm_benchmark_info_get_decode_token_count_at(
            IntPtr benchmark_info, int index);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern double litert_lm_benchmark_info_get_prefill_tokens_per_sec_at(
            IntPtr benchmark_info, int index);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern double litert_lm_benchmark_info_get_decode_tokens_per_sec_at(
            IntPtr benchmark_info, int index);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_engine_tokenize(
            IntPtr engine, [MarshalAs(UnmanagedType.LPStr)] string text);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_tokenize_result_delete(IntPtr result);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_tokenize_result_get_tokens(IntPtr result);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern UIntPtr litert_lm_tokenize_result_get_num_tokens(IntPtr result);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_engine_detokenize(
            IntPtr engine, int[] tokens, UIntPtr num_tokens);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_detokenize_result_delete(IntPtr result);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_detokenize_result_get_string(IntPtr result);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_token_union_delete(IntPtr token_union);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern LiteRtLmTokenUnionType litert_lm_token_union_get_type(
            IntPtr token_union);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_token_union_get_string(IntPtr token_union);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int litert_lm_token_union_get_ids(
            IntPtr token_union, out IntPtr out_tokens, out UIntPtr out_num_tokens);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_token_unions_delete(IntPtr tokens);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern UIntPtr litert_lm_token_unions_get_num_tokens(IntPtr tokens);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_token_unions_get_token_at(
            IntPtr tokens, UIntPtr index);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_engine_get_start_token(IntPtr engine);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_engine_get_stop_tokens(IntPtr engine);

        // --- Capabilities ---
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr litert_lm_loaded_file_create(
            [MarshalAs(UnmanagedType.LPStr)] string litertlm_path);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void litert_lm_loaded_file_delete(IntPtr loaded_file);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool litert_lm_loaded_file_has_speculative_decoding_support(
            IntPtr loaded_file);
    }
}
