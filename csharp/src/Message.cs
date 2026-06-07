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
    }

    public class Message
    {
        public Role Role { get; }
        public List<Content> Contents { get; }
        public Dictionary<string, string> Channels { get; }

        public string Text => string.Join(" ", Contents.OfType<TextContent>().Select(c => c.Text));

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
                { "role", Role.ToString().ToLowerInvariant() }
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

        public string ToJsonString()
        {
            return JsonSerializer.Serialize(ToJsonDictionary());
        }

        public override string ToString() => Text;
    }
}
