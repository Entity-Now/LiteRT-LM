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
using System.Text;
using System.Text.Json;

namespace LiteRTLM.Core
{
    public enum Role
    {
        System,
        User,
        Model,
        Tool
    }

    public abstract class Content
    {
        public abstract string Type { get; }
        public abstract Dictionary<string, string> ToJsonDictionary();
        internal abstract void WriteJson(Utf8JsonWriter writer);
    }

    public class TextContent : Content
    {
        public override string Type => "text";
        public string Text { get; }

        public TextContent(string text)
        {
            Text = text ?? string.Empty;
        }

        public override Dictionary<string, string> ToJsonDictionary()
        {
            return new Dictionary<string, string>
            {
                { "type", "text" },
                { "text", Text }
            };
        }

        internal override void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", Text);
            writer.WriteEndObject();
        }
    }

    public class ImageDataContent : Content
    {
        public override string Type => "image";
        public byte[] Data { get; }

        public ImageDataContent(byte[] data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public override Dictionary<string, string> ToJsonDictionary()
        {
            return new Dictionary<string, string>
            {
                { "type", "image" },
                { "blob", Convert.ToBase64String(Data) }
            };
        }

        internal override void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "image");
            writer.WriteString("blob", Convert.ToBase64String(Data));
            writer.WriteEndObject();
        }
    }

    public class ImageFileContent : Content
    {
        public override string Type => "image";
        public string Path { get; }

        public ImageFileContent(string path)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public override Dictionary<string, string> ToJsonDictionary()
        {
            return new Dictionary<string, string>
            {
                { "type", "image" },
                { "path", Path }
            };
        }

        internal override void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "image");
            writer.WriteString("path", Path);
            writer.WriteEndObject();
        }
    }

    public class AudioDataContent : Content
    {
        public override string Type => "audio";
        public byte[] Data { get; }

        public AudioDataContent(byte[] data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public override Dictionary<string, string> ToJsonDictionary()
        {
            return new Dictionary<string, string>
            {
                { "type", "audio" },
                { "blob", Convert.ToBase64String(Data) }
            };
        }

        internal override void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "audio");
            writer.WriteString("blob", Convert.ToBase64String(Data));
            writer.WriteEndObject();
        }
    }

    public class AudioFileContent : Content
    {
        public override string Type => "audio";
        public string Path { get; }

        public AudioFileContent(string path)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public override Dictionary<string, string> ToJsonDictionary()
        {
            return new Dictionary<string, string>
            {
                { "type", "audio" },
                { "path", Path }
            };
        }

        internal override void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "audio");
            writer.WriteString("path", Path);
            writer.WriteEndObject();
        }
    }

    public class Message
    {
        public Role Role { get; }
        public List<Content> Contents { get; }
        public Dictionary<string, string> Channels { get; }

        public string Text
        {
            get
            {
                // Avoid LINQ allocation on hot paths when there is a single text part.
                if (Contents.Count == 1 && Contents[0] is TextContent single)
                {
                    return single.Text;
                }

                return string.Join(" ", Contents.OfType<TextContent>().Select(c => c.Text));
            }
        }

        public Message(string text, Role role = Role.User, Dictionary<string, string> channels = null)
        {
            Role = role;
            Contents = new List<Content> { new TextContent(text) };
            Channels = channels ?? new Dictionary<string, string>();
        }

        public Message(List<Content> contents, Role role = Role.User, Dictionary<string, string> channels = null)
        {
            if ((contents == null || contents.Count == 0) && (channels == null || channels.Count == 0))
            {
                throw new ArgumentException("Contents and channels should not both be empty.");
            }
            Role = role;
            Contents = contents ?? new List<Content>();
            Channels = channels ?? new Dictionary<string, string>();
        }

        public Dictionary<string, object> ToJsonDictionary()
        {
            var dict = new Dictionary<string, object>
            {
                { "role", RoleToJson(Role) }
            };

            if (Contents.Count > 0)
            {
                dict["content"] = Contents.Select(c => c.ToJsonDictionary()).ToList();
            }

            if (Channels.Count > 0)
            {
                dict["channels"] = Channels;
            }

            return dict;
        }

        /// <summary>
        /// Fast path: stream JSON directly without Dictionary&lt;string, object&gt; intermediate graphs.
        /// </summary>
        public string ToJsonString()
        {
            // Common case: plain user/model text message.
            if (Contents.Count == 1 &&
                Contents[0] is TextContent textOnly &&
                Channels.Count == 0)
            {
                return WriteSimpleTextMessage(Role, textOnly.Text);
            }

            using (var stream = new MemoryStream(256))
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    WriteJson(writer);
                }

                return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
            }
        }

        internal void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("role", RoleToJson(Role));

            if (Contents.Count > 0)
            {
                writer.WritePropertyName("content");
                writer.WriteStartArray();
                for (int i = 0; i < Contents.Count; i++)
                {
                    Contents[i].WriteJson(writer);
                }
                writer.WriteEndArray();
            }

            if (Channels.Count > 0)
            {
                writer.WritePropertyName("channels");
                writer.WriteStartObject();
                foreach (var kv in Channels)
                {
                    writer.WriteString(kv.Key, kv.Value);
                }
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        internal static string RoleToJson(Role role)
        {
            switch (role)
            {
                case Role.System: return "system";
                case Role.User: return "user";
                case Role.Tool: return "tool";
                case Role.Model:
                default: return "model";
            }
        }

        private static string WriteSimpleTextMessage(Role role, string text)
        {
            // Hot path: single Utf8JsonWriter pass, no Dictionary graph.
            using (var stream = new MemoryStream(128 + (text?.Length ?? 0) * 2))
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("role", RoleToJson(role));
                    writer.WritePropertyName("content");
                    writer.WriteStartArray();
                    writer.WriteStartObject();
                    writer.WriteString("type", "text");
                    writer.WriteString("text", text ?? string.Empty);
                    writer.WriteEndObject();
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
            }
        }

        public override string ToString() => Text;
    }
}
