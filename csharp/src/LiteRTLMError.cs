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
    public class LiteRTLMException : Exception
    {
        public LiteRTLMException(string message) : base(message) { }
        public LiteRTLMException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class LiteRTLMEngineException : LiteRTLMException
    {
        public LiteRTLMEngineException(string message) : base(message) { }
        public LiteRTLMEngineException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class LiteRTLMConversationException : LiteRTLMException
    {
        public LiteRTLMConversationException(string message) : base(message) { }
        public LiteRTLMConversationException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class LiteRTLMConfigException : LiteRTLMException
    {
        public LiteRTLMConfigException(string message) : base(message) { }
        public LiteRTLMConfigException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class LiteRTLMToolException : LiteRTLMException
    {
        public LiteRTLMToolException(string message) : base(message) { }
        public LiteRTLMToolException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class LiteRTLMMessageException : LiteRTLMException
    {
        public LiteRTLMMessageException(string message) : base(message) { }
        public LiteRTLMMessageException(string message, Exception innerException) : base(message, innerException) { }
    }
}
