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
    public static class ExperimentalFlags
    {
        private static bool _optedIn = false;

        public static void OptIntoExperimentalAPIs()
        {
            if (!_optedIn)
            {
                Console.WriteLine("EXPERIMENTAL: LiteRTLM: Opting into experimental APIs....");
                _optedIn = true;
            }
        }

        private static void VerifyOptedIn()
        {
            if (!_optedIn)
            {
                throw new InvalidOperationException("LiteRTLM: Must opt into experimental APIs by calling ExperimentalFlags.OptIntoExperimentalAPIs() first.");
            }
        }

        private static bool _enableBenchmark = false;
        public static bool EnableBenchmark
        {
            get => _enableBenchmark;
            set
            {
                VerifyOptedIn();
                _enableBenchmark = value;
            }
        }

        private static bool _convertCamelToSnakeCaseInToolDescription = true;
        public static bool ConvertCamelToSnakeCaseInToolDescription
        {
            get => _convertCamelToSnakeCaseInToolDescription;
            set
            {
                VerifyOptedIn();
                _convertCamelToSnakeCaseInToolDescription = value;
            }
        }

        private static bool _enableConversationConstrainedDecoding = false;
        public static bool EnableConversationConstrainedDecoding
        {
            get => _enableConversationConstrainedDecoding;
            set
            {
                VerifyOptedIn();
                _enableConversationConstrainedDecoding = value;
            }
        }

        private static bool? _enableSpeculativeDecoding = null;
        public static bool? EnableSpeculativeDecoding
        {
            get => _enableSpeculativeDecoding;
            set
            {
                VerifyOptedIn();
                _enableSpeculativeDecoding = value;
            }
        }

        private static int? _visualTokenBudget = null;
        public static int? VisualTokenBudget
        {
            get => _visualTokenBudget;
            set
            {
                VerifyOptedIn();
                _visualTokenBudget = value;
            }
        }
    }
}
