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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace LiteRTLM.Core
{
    public class ToolManager
    {
        private class ToolMetadata
        {
            public Type ToolType;
            public Dictionary<string, (PropertyInfo Prop, ToolParamAttribute Attr)> Params;
        }

        private readonly Dictionary<string, ToolMetadata> _toolRegistry = new Dictionary<string, ToolMetadata>();
        public string ToolsJsonDescription { get; }

        public ToolManager(IEnumerable<ITool> tools)
        {
            var schemaList = new List<Dictionary<string, object>>();
            bool useSnakeCase = ExperimentalFlags.ConvertCamelToSnakeCaseInToolDescription;

            if (tools != null)
            {
                foreach (var tool in tools)
                {
                    var toolType = tool.GetType();
                    var name = tool.Name;
                    var toolNameInModel = useSnakeCase ? CamelToSnakeCase(name) : name;
                    
                    var metadata = new ToolMetadata 
                    { 
                        ToolType = toolType,
                        Params = new Dictionary<string, (PropertyInfo Prop, ToolParamAttribute Attr)>()
                    };

                    var props = toolType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var prop in props)
                    {
                        var attr = prop.GetCustomAttribute<ToolParamAttribute>();
                        if (attr != null)
                        {
                            var cleanName = useSnakeCase ? CamelToSnakeCase(prop.Name) : prop.Name;
                            metadata.Params[cleanName] = (prop, attr);
                        }
                    }

                    _toolRegistry[toolNameInModel] = metadata;
                    schemaList.Add(GetSchema(tool, useSnakeCase));
                }
            }

            ToolsJsonDescription = JsonSerializer.Serialize(schemaList);
        }

        public async Task<object> ExecuteAsync(string name, Dictionary<string, object> arguments)
        {
            if (!_toolRegistry.TryGetValue(name, out var metadata))
            {
                throw new LiteRTLMToolException($"Tool '{name}' not found.");
            }

            var toolInstance = (ITool)Activator.CreateInstance(metadata.ToolType);

            foreach (var kvp in metadata.Params)
            {
                var cleanName = kvp.Key;
                var prop = kvp.Value.Prop;
                var attr = kvp.Value.Attr;

                object val = null;
                if (arguments != null && arguments.TryGetValue(cleanName, out var jsonVal))
                {
                    val = ResolveValue(jsonVal, prop.PropertyType);
                }
                else
                {
                    if (attr.DefaultValue != null)
                    {
                        val = attr.DefaultValue;
                    }
                    else if (attr.IsRequired)
                    {
                        throw new LiteRTLMConversationException($"Required parameter '{cleanName}' was not provided for tool '{name}'.");
                    }
                }

                if (val != null)
                {
                    prop.SetValue(toolInstance, val);
                }
            }

            var result = await toolInstance.RunAsync();
            return NormalizeResult(result);
        }

        private object ResolveValue(object val, Type targetType)
        {
            if (val == null) return null;

            if (val is JsonElement elem)
            {
                switch (elem.ValueKind)
                {
                    case JsonValueKind.String:
                        if (targetType == typeof(string)) return elem.GetString();
                        break;
                    case JsonValueKind.Number:
                        if (targetType == typeof(int)) return elem.GetInt32();
                        if (targetType == typeof(long)) return elem.GetInt64();
                        if (targetType == typeof(double)) return elem.GetDouble();
                        if (targetType == typeof(float)) return (float)elem.GetDouble();
                        break;
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        if (targetType == typeof(bool)) return elem.GetBoolean();
                        break;
                    case JsonValueKind.Null:
                        return null;
                    case JsonValueKind.Array:
                        var elemType = targetType.IsArray ? targetType.GetElementType() : targetType.GenericTypeArguments.FirstOrDefault();
                        if (elemType != null)
                        {
                            var listType = typeof(List<>).MakeGenericType(elemType);
                            var list = (IList)Activator.CreateInstance(listType);
                            foreach (var item in elem.EnumerateArray())
                            {
                                list.Add(ResolveValue(item, elemType));
                            }
                            if (targetType.IsArray)
                            {
                                var arr = Array.CreateInstance(elemType, list.Count);
                                list.CopyTo(arr, 0);
                                return arr;
                            }
                            return list;
                        }
                        break;
                }
            }

            try
            {
                if (targetType.IsInstanceOfType(val)) return val;
                return Convert.ChangeType(val, targetType);
            }
            catch
            {
                return val;
            }
        }

        private Dictionary<string, object> GetSchema(ITool tool, bool useSnakeCase)
        {
            var properties = new Dictionary<string, object>();
            var requiredFields = new List<string>();

            var toolType = tool.GetType();
            var props = toolType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in props)
            {
                var attr = prop.GetCustomAttribute<ToolParamAttribute>();
                if (attr == null) continue;

                var cleanName = useSnakeCase ? CamelToSnakeCase(prop.Name) : prop.Name;
                var paramSchema = GetJsonSchema(prop.PropertyType);

                if (!string.IsNullOrEmpty(attr.Description))
                {
                    paramSchema["description"] = attr.Description;
                }

                properties[cleanName] = paramSchema;

                if (attr.IsRequired)
                {
                    requiredFields.Add(cleanName);
                }
            }

            var toolName = useSnakeCase ? CamelToSnakeCase(tool.Name) : tool.Name;

            var parameters = new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", properties }
            };

            if (requiredFields.Count > 0)
            {
                parameters["required"] = requiredFields;
            }

            var functionBody = new Dictionary<string, object>
            {
                { "name", toolName },
                { "description", tool.Description }
            };

            if (properties.Count > 0)
            {
                functionBody["parameters"] = parameters;
            }

            return new Dictionary<string, object>
            {
                { "type", "function" },
                { "function", functionBody }
            };
        }

        private Dictionary<string, object> GetJsonSchema(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null)
            {
                var schema = GetJsonSchema(underlyingType);
                schema["nullable"] = true;
                return schema;
            }

            if (type == typeof(string)) return new Dictionary<string, object> { { "type", "string" } };
            if (type == typeof(int) || type == typeof(long) || type == typeof(short)) return new Dictionary<string, object> { { "type", "integer" } };
            if (type == typeof(bool)) return new Dictionary<string, object> { { "type", "boolean" } };
            if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return new Dictionary<string, object> { { "type", "number" } };

            if (type.IsArray)
            {
                return new Dictionary<string, object>
                {
                    { "type", "array" },
                    { "items", GetJsonSchema(type.GetElementType()) }
                };
            }
            if (typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
            {
                return new Dictionary<string, object>
                {
                    { "type", "array" },
                    { "items", GetJsonSchema(type.GenericTypeArguments[0]) }
                };
            }

            return new Dictionary<string, object> { { "type", "object" } };
        }

        private object NormalizeResult(object value)
        {
            if (value == null) return string.Empty;

            if (value is string || value is int || value is long || value is double || value is float || value is bool)
            {
                return value;
            }

            if (value is IDictionary dict)
            {
                var normalizedDict = new Dictionary<string, object>();
                foreach (DictionaryEntry entry in dict)
                {
                    normalizedDict[entry.Key.ToString()] = NormalizeResult(entry.Value);
                }
                return normalizedDict;
            }

            if (value is IEnumerable seq)
            {
                var list = new List<object>();
                foreach (var item in seq)
                {
                    list.Add(NormalizeResult(item));
                }
                return list;
            }

            return value.ToString();
        }

        private static string CamelToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (char.IsUpper(c))
                {
                    if (i > 0) sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
