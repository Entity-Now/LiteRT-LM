package(default_visibility = ["//visibility:public"])

# 这个目标会作为你的 Windows (.dll) 和 macOS (.dylib) 的专属生成器
cc_binary(
    name = "litert_lm",
    linkshared = True, # 关键：强行让 Bazel 把官方的静态组件融合成动态链接库
    defines = ["LITERT_BUILD_DLL"],
    deps = [
        # 直接拉取官方在 c 目录下维护的成熟稳定的静态库目标
        "//c:engine", 
    ],
)
