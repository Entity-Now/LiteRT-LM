# Copyright 2026 Google LLC
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

package(default_visibility = ["//visibility:public"])

cc_binary(
    name = "CLiteRTLM",
    linkshared = True,
    visibility = ["//visibility:public"],
    deps = [
        "//c:engine",
        "//schema/capabilities:capabilities_c",
    ],
)

# Packages the built native shared library and headers
genrule(
    name = "CLiteRTLM_package",
    srcs = [
        ":CLiteRTLM",
        "//c:engine.h",
        "//schema/capabilities:capabilities_c.h",
    ],
    outs = ["CLiteRTLM_csharp.zip"],
    cmd = """
        mkdir -p CLiteRTLM_csharp/native
        mkdir -p CLiteRTLM_csharp/include
        
        # Copy the native library
        cp $(location :CLiteRTLM) CLiteRTLM_csharp/native/
        
        # Copy headers
        cp $(location //c:engine.h) CLiteRTLM_csharp/include/
        cp $(location //schema/capabilities:capabilities_c.h) CLiteRTLM_csharp/include/
        
        # Zip it up
        zip -r $@ CLiteRTLM_csharp
    """,
)
