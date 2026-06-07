package(default_visibility = ["//visibility:public"])

# 专门为 Windows 准备的导出配置，强行把官方 c/engine.h 里的函数导出来
outputs = select({
    "@bazel_tools//src/conditions:windows": ["/DEF:$(location :litert_lm.def)"],
    "//conditions:default": [],
})

cc_binary(
    name = "litert_lm",
    srcs = [
        "win_dummy.cc",
        "litert_lm.def",
    ],
    linkopts = outputs,
    linkshared = True, # 强制生成动态库
    deps = [
        "//c:engine", # 官方底座
    ],
)
